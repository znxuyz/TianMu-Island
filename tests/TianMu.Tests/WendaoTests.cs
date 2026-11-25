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
}
