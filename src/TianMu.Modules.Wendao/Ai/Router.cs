using System.Text.RegularExpressions;

namespace TianMu.Modules.Wendao.Ai;

/// <summary>問題的性質。</summary>
public enum TaskKind { General, Quick, Translate, Code, Reasoning, LongText, Vision }

/// <summary>
/// 自動挑模型：先看問題屬於哪一類，再依各模型的名稱特徵、速度、能讀多長來排名。
/// 回傳一串候選，第一個失敗（額度用完、暫時故障）就換下一個。純邏輯，方便測試。
/// </summary>
public static partial class Router
{
    public static string Label(TaskKind k) => k switch
    {
        TaskKind.Quick => "快問快答",
        TaskKind.Translate => "翻譯潤稿",
        TaskKind.Code => "寫程式",
        TaskKind.Reasoning => "推理分析",
        TaskKind.LongText => "長文件",
        TaskKind.Vision => "看圖",
        _ => "一般",
    };

    public static TaskKind Classify(string prompt, bool hasImage, int historyChars = 0)
    {
        if (hasImage) return TaskKind.Vision;
        string p = prompt.Trim();
        if (p.Length + historyChars > 24_000) return TaskKind.LongText;
        if (CodeHint().IsMatch(p)) return TaskKind.Code;
        if (TranslateHint().IsMatch(p)) return TaskKind.Translate;
        if (ReasonHint().IsMatch(p)) return TaskKind.Reasoning;
        if (p.Length <= 40 && !p.Contains('\n')) return TaskKind.Quick;
        return TaskKind.General;
    }

    [GeneratedRegex(@"```|\b(code|function|class|bug|error|exception|stack ?trace|regex|sql|python|javascript|typescript|c#|java|rust|golang|html|css|api|json|yaml|powershell|bash|excel 公式)\b|程式|函式|函數|報錯|錯誤訊息|除錯|寫一個.*(腳本|程式)|正規表示式", RegexOptions.IgnoreCase)]
    private static partial Regex CodeHint();

    [GeneratedRegex(@"翻譯|譯成|翻成|translate|潤稿|潤飾|改寫|校稿|修飾|摘要|summari[sz]e|重點整理", RegexOptions.IgnoreCase)]
    private static partial Regex TranslateHint();

    [GeneratedRegex(@"為什麼|證明|推導|分析|比較|計算|數學|邏輯|規劃|策略|優缺點|步驟|prove|why|analy[sz]e|compare|reason|plan", RegexOptions.IgnoreCase)]
    private static partial Regex ReasonHint();

    /// <summary>
    /// 是不是要「畫一張圖」（例如「幫我畫一隻貓」「生成一張寶箱怪的圖」）。
    /// 流程圖、圖表、架構圖這類其實是要文字或程式的，不算。
    /// </summary>
    public static bool WantsImage(string prompt)
    {
        string p = prompt.Trim();
        if (p.Length == 0 || p.Length > 300 || NotPicture().IsMatch(p)) return false;
        return DrawHint().IsMatch(p);
    }

    [GeneratedRegex(@"^(請|麻煩)?(幫我|替我|給我|幫忙)?\s*(畫|繪製)|(生成|產生|設計)(一|幾|兩|三)?(張|幅|個)?.{0,40}(圖片|圖像|插畫|插圖|照片|頭像|海報|桌布|壁紙|的圖|張圖)|(做|弄|來)(一|幾|兩|三)(張|幅).{0,40}(圖|插畫|照片|頭像|海報|桌布)|^(draw|paint|sketch)\b|\b(generate|create|make)\b.{0,30}\b(image|picture|illustration|photo|drawing)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DrawHint();

    [GeneratedRegex(@"流程圖|架構圖|心智圖|圖表|甘特圖|UML|ER ?圖|關係圖|示意圖的程式|mermaid|graphviz|chart|diagram|表格", RegexOptions.IgnoreCase)]
    private static partial Regex NotPicture();

    /// <summary>依問題性質把模型排序，最適合的在前面。</summary>
    public static List<ModelInfo> Rank(IEnumerable<ModelInfo> models, TaskKind kind, Func<string, double>? speedOf = null)
    {
        speedOf ??= id => Provider.Find(id)?.Speed ?? 0.5;
        return models
            .Where(m => kind != TaskKind.Vision || m.Vision)
            .Select(m => (Model: m, Score: Score(m, kind, speedOf(m.ProviderId))))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Model.Key, StringComparer.Ordinal)
            .Select(x => x.Model)
            .ToList();
    }

    public static double Score(ModelInfo m, TaskKind kind, double speed)
    {
        string id = m.Id.ToLowerInvariant();
        bool Has(params string[] keys) => keys.Any(id.Contains);
        double size = ParamsBillions(id);
        double big = size > 0 ? Math.Min(1, Math.Log10(size) / 2.5) : Has("pro", "large", "max", "120b", "235b", "405b") ? 0.9 : Has("flash-lite", "mini", "small", "nano", "tiny") ? 0.3 : 0.6;
        double ctx = m.ContextLength > 0 ? Math.Min(1, Math.Log10(Math.Max(m.ContextLength, 4096)) / 6) : 0.5;
        double s = 0;
        switch (kind)
        {
            case TaskKind.Code:
                s = big * 2 + (Has("coder", "codestral", "devstral", "code") ? 2.5 : 0) + (Has("qwen3", "deepseek", "gpt-oss", "kimi", "glm") ? 1.2 : 0) + (Has("gemini-2.5-pro", "gemini-3") ? 1.5 : 0) + speed * 0.5;
                break;
            case TaskKind.Reasoning:
                s = big * 2.5 + (Has("r1", "reason", "think", "qwq", "gpt-oss", "deepseek") ? 1.8 : 0) + (Has("pro") ? 1.2 : 0) + speed * 0.3;
                break;
            case TaskKind.LongText:
                s = ctx * 4 + big + (Has("gemini") ? 1.5 : 0);
                break;
            case TaskKind.Vision:
                s = big * 1.5 + (Has("gemini") ? 2 : 0) + speed * 0.5;
                break;
            case TaskKind.Translate:
                s = big * 1.2 + speed * 1.5 + (Has("gemini", "qwen", "mistral") ? 1 : 0);
                break;
            case TaskKind.Quick:
                s = speed * 3 + big * 0.8 + (Has("flash", "instant", "8b", "mini") ? 0.8 : 0) - (Has("r1", "think", "reason", "qwq") ? 1.5 : 0);
                break;
            default:
                s = big * 1.8 + speed * 1.2 + (Has("gemini", "llama-3.3", "qwen3", "deepseek") ? 0.8 : 0);
                break;
        }
        // 預覽版、實驗版不穩定，稍微往後排
        if (Has("preview", "exp", "experimental")) s -= 0.4;
        return s;
    }

    /// <summary>從名稱找參數量，例如「llama-3.3-70b」→ 70。</summary>
    public static double ParamsBillions(string id)
    {
        var m = SizeRegex().Matches(id);
        double best = 0;
        foreach (Match x in m)
            if (double.TryParse(x.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v))
                best = Math.Max(best, v);
        return best;
    }

    [GeneratedRegex(@"(?<![\d.])(\d+(?:\.\d+)?)b(?![a-z])", RegexOptions.IgnoreCase)]
    private static partial Regex SizeRegex();
}
