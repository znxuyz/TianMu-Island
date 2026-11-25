using System.Text.Json;
using TianMu.Modules.Wendao.Ai;

namespace TianMu.Modules.Wendao.Web;

/// <summary>網頁模式的一家 AI：直接開官方網站，用你自己的帳號登入（免費方案或訂閱都照用）。</summary>
public sealed record WebSite(string Id, string Name, string Url, string Strength, bool Video = false);

/// <summary>網頁模式的站台清單、自動挑選規則與「填入問題」的腳本（純邏輯，方便測試）。</summary>
public static class WebSites
{
    public static readonly WebSite ChatGpt = new("chatgpt", "ChatGPT", "https://chatgpt.com/", "一般問答、寫作、推理");
    public static readonly WebSite Gemini = new("gemini", "Gemini", "https://gemini.google.com/app", "看圖、長文件、翻譯；用 Google 帳號");
    public static readonly WebSite Claude = new("claude", "Claude", "https://claude.ai/new", "寫程式、長篇寫作與分析");
    public static readonly WebSite Copilot = new("copilot", "Copilot", "https://copilot.microsoft.com/", "微軟帳號；附網路搜尋");
    public static readonly WebSite DeepSeek = new("deepseek", "DeepSeek", "https://chat.deepseek.com/", "數學、推理、程式");
    public static readonly WebSite Grok = new("grok", "Grok", "https://grok.com/", "即時話題");
    public static readonly WebSite Perplexity = new("perplexity", "Perplexity", "https://www.perplexity.ai/", "查資料、附來源");

    // 生成影片：免費的 API 幾乎沒有，用各家網站每天送的免費點數最實際
    public static readonly WebSite Kling = new("kling", "可靈", "https://klingai.com/", "文字或圖片生成短影片；每天送免費點數", Video: true);
    public static readonly WebSite Hailuo = new("hailuo", "海螺", "https://hailuoai.video/", "文字或圖片生成短影片；有免費額度", Video: true);
    public static readonly WebSite Pika = new("pika", "Pika", "https://pika.art/", "短影片、特效；每月送免費點數", Video: true);

    public static readonly IReadOnlyList<WebSite> All = [ChatGpt, Gemini, Claude, Copilot, DeepSeek, Grok, Perplexity, Kling, Hailuo, Pika];

    public static WebSite? Find(string id) => All.FirstOrDefault(s => s.Id == id);

    /// <summary>自動挑選：依問題性質決定交給哪一家。</summary>
    public static WebSite For(TaskKind kind) => kind switch
    {
        TaskKind.Code => Claude,
        TaskKind.Reasoning => DeepSeek,
        TaskKind.Vision or TaskKind.LongText or TaskKind.Translate => Gemini,
        _ => ChatGpt,
    };

    /// <summary>
    /// 把問題填進網頁的輸入框（不按送出，由你自己按）。依序找各家常見的輸入框；
    /// 一般 textarea 用原生 setter 讓網頁框架察覺，富文字編輯器用 insertText。
    /// 回傳 "ok" 或 "none"（找不到輸入框，可能還沒登入或網站改版）。
    /// </summary>
    public static string FillScript(string text) => """
        (function (t) {
          const selectors = ['#prompt-textarea', 'div.ProseMirror[contenteditable="true"]', 'rich-textarea .ql-editor',
                             'textarea', '[contenteditable="true"]'];
          let el = null;
          for (const s of selectors) {
            const found = [...document.querySelectorAll(s)].filter(e => e.offsetParent !== null && !e.disabled && !e.readOnly);
            if (found.length) { el = found[found.length - 1]; break; }
          }
          if (!el) return 'none';
          el.focus();
          if (el.tagName === 'TEXTAREA' || el.tagName === 'INPUT') {
            const setter = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(el), 'value').set;
            setter.call(el, t);
            el.dispatchEvent(new Event('input', { bubbles: true }));
          } else {
            document.execCommand('selectAll', false, null);
            document.execCommand('insertText', false, t);
          }
          return 'ok';
        })(
        """ + JsonSerializer.Serialize(text) + ");";
}
