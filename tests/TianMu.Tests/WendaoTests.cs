using System.IO;
using System.Net;
using System.Text.Json;
using TianMu.Modules.Wendao.Ai;
using TianMu.Modules.Wendao.Store;
using TianMu.Modules.Wendao.Views;
using TianMu.Modules.Wendao.Web;

namespace TianMu.Tests;

public class WendaoTests
{
    [Fact]
    public void OpenRouter_list_keeps_only_free_chat_models()
    {
        const string json = """
        {"data":[
          {"id":"deepseek/deepseek-r1:free","name":"DeepSeek: R1 (free)","context_length":163840,"architecture":{"input_modalities":["text"]},"pricing":{"prompt":"0","completion":"0"}},
          {"id":"openai/gpt-5","name":"OpenAI: GPT-5","context_length":400000,"pricing":{"prompt":"0.00000125","completion":"0.00001"}},
          {"id":"google/gemma-3-27b-it:free","name":"Gemma 3 27B","context_length":96000,"architecture":{"input_modalities":["text","image"]},"pricing":{"prompt":"0","completion":"0"}},
          {"id":"openai/text-embedding-3-small","pricing":{"prompt":"0","completion":"0"}}
        ]}
        """;
        var models = ModelCatalog.Parse("openrouter", json);
        Assert.Equal(["deepseek/deepseek-r1:free", "google/gemma-3-27b-it:free"], models.Select(m => m.Id).Order());
        Assert.True(models.Single(m => m.Id.StartsWith("google")).Vision);
        Assert.Equal(163840, models.Single(m => m.Id.StartsWith("deepseek")).ContextLength);
    }

    [Fact]
    public void Gemini_list_strips_prefix_and_drops_non_chat_models()
    {
        const string json = """
        {"object":"list","data":[
          {"id":"models/gemini-2.5-flash","object":"model"},
          {"id":"models/gemini-embedding-001","object":"model"},
          {"id":"models/imagen-4.0-generate-001","object":"model"},
          {"id":"models/gemini-2.5-flash-preview-tts","object":"model"}
        ]}
        """;
        var m = Assert.Single(ModelCatalog.Parse("gemini", json));
        Assert.Equal("gemini-2.5-flash", m.Id);
        Assert.True(m.Vision);
        Assert.Equal(1_000_000, m.ContextLength);
    }

    [Fact]
    public void Groq_list_drops_speech_and_guard_models()
    {
        const string json = """{"data":[{"id":"llama-3.3-70b-versatile","context_window":131072},{"id":"whisper-large-v3"},{"id":"meta-llama/llama-guard-4-12b"},{"id":"llama-3.1-8b-instant","context_window":131072}]}""";
        Assert.Equal(["llama-3.1-8b-instant", "llama-3.3-70b-versatile"], ModelCatalog.Parse("groq", json).Select(x => x.Id));
    }

    [Theory]
    [InlineData("你好", false, TaskKind.Quick)]
    [InlineData("幫我把這段翻譯成英文：今天天氣很好，我們去散步吧，順便買點東西回家", false, TaskKind.Translate)]
    [InlineData("這段 python 為什麼會報錯？\n```\nprint(x\n```", false, TaskKind.Code)]
    [InlineData("請分析台積電和聯電的商業模式差異，並比較它們未來五年的優缺點與風險", false, TaskKind.Reasoning)]
    [InlineData("這是什麼？", true, TaskKind.Vision)]
    public void Questions_are_classified(string q, bool image, TaskKind kind) =>
        Assert.Equal(kind, Router.Classify(q, image));

    [Fact]
    public void Long_conversations_go_to_long_context_models()
    {
        Assert.Equal(TaskKind.LongText, Router.Classify("幫我整理重點", false, historyChars: 30_000));
        var models = new[]
        {
            new ModelInfo("groq", "llama-3.1-8b-instant", "8b", 131072, false),
            new ModelInfo("gemini", "gemini-2.5-flash", "flash", 1_000_000, true),
        };
        Assert.Equal("gemini-2.5-flash", Router.Rank(models, TaskKind.LongText)[0].Id);
    }

    [Fact]
    public void Ranking_prefers_fast_models_for_quick_questions_and_big_ones_for_code()
    {
        var models = new[]
        {
            new ModelInfo("groq", "llama-3.1-8b-instant", "8b", 131072, false),
            new ModelInfo("openrouter", "qwen/qwen3-coder:free", "coder", 262144, false),
            new ModelInfo("openrouter", "deepseek/deepseek-r1:free", "r1", 163840, false),
        };
        Assert.Equal("groq", Router.Rank(models, TaskKind.Quick)[0].ProviderId);
        Assert.Equal("qwen/qwen3-coder:free", Router.Rank(models, TaskKind.Code)[0].Id);
        Assert.Empty(Router.Rank(models, TaskKind.Vision));     // 沒有看得懂圖的就不排
        Assert.Equal(70, Router.ParamsBillions("llama-3.3-70b-versatile"));
        Assert.Equal(0, Router.ParamsBillions("gemini-2.5-flash"));
    }

    [Fact]
    public void Auto_mode_spreads_candidates_across_providers()
    {
        var conductor = new Conductor(new ChatClient(), _ => "k");
        var field = typeof(Conductor).GetField("_models", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var dict = (Dictionary<string, List<ModelInfo>>)field.GetValue(conductor)!;
        dict["openrouter"] = Enumerable.Range(1, 5).Select(i => new ModelInfo("openrouter", $"model-{i}0b:free", "", 32000, false)).ToList();
        dict["groq"] = [new ModelInfo("groq", "llama-3.3-70b-versatile", "", 131072, false)];
        var auto = conductor.Candidates(Conductor.Auto, TaskKind.General);
        Assert.True(auto.Count(m => m.ProviderId == "openrouter") <= 2);
        Assert.Contains(auto, m => m.ProviderId == "groq");

        var manual = conductor.Candidates("openrouter/model-10b:free", TaskKind.General);
        Assert.Equal("model-10b:free", manual[0].Id);           // 手動選的永遠第一個
    }

    [Fact]
    public void Request_body_is_openai_compatible_with_images_as_data_urls()
    {
        var body = ChatClient.BuildRequest("gemini-2.5-flash",
            [new ChatMessage(Role.System, "sys"), new ChatMessage(Role.User, "看圖", [1, 2, 3], "image/jpeg")]);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("stream").GetBoolean());
        var msgs = root.GetProperty("messages");
        Assert.Equal("system", msgs[0].GetProperty("role").GetString());
        var parts = msgs[1].GetProperty("content");
        Assert.Equal("看圖", parts[0].GetProperty("text").GetString());
        Assert.Equal("data:image/jpeg;base64,AQID", parts[1].GetProperty("image_url").GetProperty("url").GetString());
    }

    [Fact]
    public void Stream_lines_are_parsed()
    {
        Assert.True(ChatClient.ParseSseLine("data: {\"choices\":[{\"delta\":{\"content\":\"道\"}}]}", out var piece, out var err, out var done));
        Assert.Equal("道", piece);
        Assert.Null(err);
        Assert.False(done);
        Assert.True(ChatClient.ParseSseLine("data: [DONE]", out _, out _, out done));
        Assert.True(done);
        Assert.False(ChatClient.ParseSseLine(": OPENROUTER PROCESSING", out _, out _, out _));
        Assert.True(ChatClient.ParseSseLine("data: {\"error\":{\"message\":\"Rate limit exceeded\"}}", out _, out err, out _));
        Assert.Equal("Rate limit exceeded", err);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.NotFound, true)]
    public void Errors_that_another_provider_could_fix_move_on(HttpStatusCode code, bool next) =>
        Assert.Equal(next, ChatClient.Error(Provider.Groq, code, """{"error":{"message":"x"}}""").TryNext);

    [Fact]
    public void Code_blocks_are_split_out_of_answers()
    {
        var parts = MessageView.Split("說明\n```python\nprint(1)\n```\n結尾");
        Assert.Equal(3, parts.Count);
        Assert.False(parts[0].IsCode);
        Assert.True(parts[1].IsCode);
        Assert.Equal("python", parts[1].Lang);
        Assert.Equal("print(1)\n", parts[1].Text);
        Assert.Equal("結尾\n", parts[2].Text);
    }

    [Fact]
    public void Context_keeps_recent_messages_and_puts_the_image_on_the_last_question()
    {
        var history = new List<SavedMessage>
        {
            new() { Role = Role.User, Text = new string('舊', 70_000) },
            new() { Role = Role.Assistant, Text = "答" },
            new() { Role = Role.User, Text = "新問題" },
        };
        var ctx = ChatWindow.BuildContext(history, [9], "image/png");
        Assert.Equal(Role.System, ctx[0].Role);
        Assert.Equal(3, ctx.Count);                       // 太舊太長的被砍掉
        Assert.Equal("新問題", ctx[^1].Text);
        Assert.NotNull(ctx[^1].Image);
        Assert.Null(ctx[1].Image);
        Assert.Equal("新的對話", Conversation.TitleFrom("  \n "));
        Assert.Equal(25, Conversation.TitleFrom(new string('字', 40)).Length);
    }

    [Fact]
    public void Web_mode_picks_a_site_by_task()
    {
        Assert.Equal("claude", WebSites.For(TaskKind.Code).Id);
        Assert.Equal("gemini", WebSites.For(TaskKind.Vision).Id);
        Assert.Equal("gemini", WebSites.For(TaskKind.LongText).Id);
        Assert.Equal("chatgpt", WebSites.For(TaskKind.Quick).Id);
        Assert.All(WebSites.All, s => Assert.StartsWith("https://", s.Url));
        Assert.Equal(WebSites.All.Count, WebSites.All.Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void Fill_script_embeds_the_question_safely()
    {
        const string question = "他說\"好\"</script>\n第二行";
        string js = WebSites.FillScript(question);
        string arg = js[(js.LastIndexOf("})(", StringComparison.Ordinal) + 3)..].Trim().TrimEnd(';').TrimEnd(')');
        Assert.Equal(question, JsonSerializer.Deserialize<string>(arg));   // 原封不動地傳進網頁
        Assert.DoesNotContain("</script>", js);                            // 不會原樣出現在腳本裡
        Assert.DoesNotContain(".click()", js);                             // 只填字，不按送出
    }

    [Fact]
    public void Text_files_are_read_and_attached_after_the_question()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wendao-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string txt = Path.Combine(dir, "筆記.md");
            File.WriteAllText(txt, "# 標題\n內容");
            var a = FileReader.Read(txt);
            Assert.Equal(AttachmentKind.Text, a.Kind);
            Assert.Contains("內容", a.Text);
            string prompt = FileReader.Compose("幫我摘要", [a]);
            Assert.StartsWith("幫我摘要", prompt);
            Assert.Contains("〔附件：筆記.md〕", prompt);
            Assert.True(FileReader.CanRead("a.PDF"));
            Assert.True(FileReader.CanRead("報告.docx"));
            Assert.False(FileReader.CanRead("程式.exe"));
            Assert.Throws<InvalidDataException>(() => FileReader.Read(Path.Combine(dir, "空.txt").Also(p => File.WriteAllText(p, "  "))));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Big5_text_files_are_decoded()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var big5 = System.Text.Encoding.GetEncoding(950).GetBytes("天幕島");
        Assert.Equal("天幕島", FileReader.ReadText(big5));
    }

    [Fact]
    public void Compare_answers_stay_out_of_the_follow_up_context_and_files_go_in()
    {
        var history = new List<SavedMessage>
        {
            new() { Role = Role.User, Text = "問題", FileText = "\n〔附件：a.txt〕內容" },
            new() { Role = Role.Assistant, Text = "甲家答案" },
            new() { Role = Role.Assistant, Text = "乙家答案", Alternate = true },
            new() { Role = Role.User, Text = "追問" },
        };
        var ctx = ChatWindow.BuildContext(history, null, "image/png");
        Assert.DoesNotContain(ctx, m => m.Text == "乙家答案");
        Assert.Contains(ctx, m => m.Text.Contains("〔附件：a.txt〕"));
        Assert.Equal("追問", ctx[^1].Text);
    }

    [Fact]
    public void Markdown_marks_are_removed_for_reading_aloud_and_export_has_both_sides()
    {
        Assert.Equal("重點\n第一點 很重要", Markdown.Plain("## 重點\n- 第一點 **很重要**"));
        var c = new Conversation { Title = "測試" };
        c.Messages.Add(new SavedMessage { Role = Role.User, Text = "你好", Files = ["a.pdf"] });
        c.Messages.Add(new SavedMessage { Role = Role.Assistant, Text = "嗨", Model = "gemini-2.5-flash · Google Gemini" });
        string md = ChatWindow.ToMarkdown(c);
        Assert.Contains("# 測試", md);
        Assert.Contains("## 問\n\n你好", md);
        Assert.Contains("附件：a.pdf", md);
        Assert.Contains("## 答（gemini-2.5-flash · Google Gemini）\n\n嗨", md);
    }

    [Fact]
    public void Image_requests_are_built_and_parsed()
    {
        string url = ImageGenerator.PollinationsUrl("一隻貓 & 月亮", 1024, 768, 42);
        Assert.StartsWith("https://gen.pollinations.ai/image/", url);
        Assert.DoesNotContain(" ", url);
        Assert.Contains("%26", url);                       // & 被編碼，不會截斷提示詞
        Assert.Contains("width=1024&height=768&seed=42", url);

        Assert.Equal(("acc123", "tok:en"), ImageGenerator.SplitCloudflareKey(" acc123 : tok:en "));
        Assert.Null(ImageGenerator.SplitCloudflareKey("沒有冒號"));
        Assert.Null(ImageGenerator.SplitCloudflareKey(null));

        Assert.Equal(new byte[] { 1, 2, 3 }, ImageGenerator.ParseCloudflare("""{"result":{"image":"AQID"},"success":true}"""));
        var (bytes, mime) = ImageGenerator.ParseGemini("""{"candidates":[{"content":{"parts":[{"text":"好"},{"inlineData":{"mimeType":"image/png","data":"AQID"}}]}}]}""");
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
        Assert.Equal("image/png", mime);
        Assert.ThrowsAny<Exception>(() => ImageGenerator.ParseGemini("""{"candidates":[]}"""));

        Assert.Equal((1344, 768), ImageGenerator.SizeOf("16:9"));
        Assert.Equal("9:16", ImageGenerator.AspectName(768, 1344));
        Assert.Equal("1:1", ImageGenerator.AspectName(1024, 1024));
    }

    [Fact]
    public void Drawing_stays_out_of_the_chat_context_and_video_sites_are_listed()
    {
        var history = new List<SavedMessage>
        {
            new() { Role = Role.User, Text = "畫：一隻貓", ImageOnly = true },
            new() { Role = Role.Assistant, Text = "提示詞：a cat", ImageOnly = true, ImagePath = "x.png" },
            new() { Role = Role.User, Text = "你好" },
        };
        var ctx = ChatWindow.BuildContext(history, null, "image/png");
        Assert.Equal(2, ctx.Count);                       // 系統提示＋「你好」
        Assert.Contains(WebSites.All, s => s.Video && s.Id == "kling");
        Assert.All(WebSites.All.Where(s => s.Video), s => Assert.StartsWith("https://", s.Url));
    }

    [Theory]
    [InlineData("幫我生成一張寶箱怪的圖", true)]
    [InlineData("畫一隻在月光下的貓", true)]
    [InlineData("幫我畫一張賽博龐克風的台北", true)]
    [InlineData("來一張可愛的柴犬頭像", true)]
    [InlineData("Draw a dragon over the sea", true)]
    [InlineData("generate an image of a castle", true)]
    [InlineData("幫我畫一個系統架構圖", false)]
    [InlineData("生成一張 Excel 圖表", false)]
    [InlineData("如何做一個網站的首頁圖片輪播", false)]
    [InlineData("這段程式為什麼會出錯", false)]
    public void Drawing_requests_typed_in_the_chat_box_are_recognised(string text, bool expected) =>
        Assert.Equal(expected, Router.WantsImage(text));

    [Fact]
    public void Keycap_emoji_numbers_are_tidied() =>
        Assert.Equal("5. 下一步", Markdown.Tidy("5\uFE0F\u20E3 下一步"));
}

internal static class TestFluent
{
    public static T Also<T>(this T value, Action<T> act) { act(value); return value; }
}
