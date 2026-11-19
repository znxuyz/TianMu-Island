namespace TianMu.Modules.Wendao.Ai;

/// <summary>一家 AI 服務：都走「相容 OpenAI」的對話介面，所以同一套程式就能連。</summary>
public sealed record Provider(
    string Id,
    string Name,
    string BaseUrl,
    bool NeedsKey,
    string? KeyUrl,
    string Note,
    double Speed)        // 回應速度的粗估（0～1），自動挑選時「快問快答」會偏好快的
{
    public static readonly Provider Gemini = new("gemini", "Google Gemini", "https://generativelanguage.googleapis.com/v1beta/openai/", true,
        "https://aistudio.google.com/apikey",
        "用 Google 帳號登入 AI Studio 就能免費取得金鑰。看得懂圖片、能讀很長的文件。注意：免費方案的內容可能被 Google 用來改進產品，別貼機密。", 0.7);

    public static readonly Provider Groq = new("groq", "Groq", "https://api.groq.com/openai/v1/", true,
        "https://console.groq.com/keys",
        "免費、速度極快，適合快問快答與翻譯；每天有次數與字數上限。", 1.0);

    public static readonly Provider OpenRouter = new("openrouter", "OpenRouter 免費模型", "https://openrouter.ai/api/v1/", true,
        "https://openrouter.ai/keys",
        "一把金鑰可用多種免費模型（DeepSeek、Qwen、Llama⋯）；問道只會用名稱帶 :free 的免費模型，不會扣款。每天約 50 次。", 0.5);

    public static readonly Provider Mistral = new("mistral", "Mistral", "https://api.mistral.ai/v1/", true,
        "https://console.mistral.ai/api-keys",
        "免費的 Experiment 方案（需驗證手機）；法文、歐語與程式能力不錯。", 0.6);

    public static readonly Provider Ollama = new("ollama", "本機 Ollama", "http://localhost:11434/v1/", false,
        "https://ollama.com/download",
        "完全在你的電腦上跑、不用網路也不用金鑰；需要先安裝 Ollama 並下載模型，速度看顯示卡。", 0.4);

    public static readonly IReadOnlyList<Provider> All = [Gemini, Groq, OpenRouter, Mistral, Ollama];

    public static Provider? Find(string id) => All.FirstOrDefault(p => p.Id == id);
}

/// <summary>一個可用的模型。</summary>
public sealed record ModelInfo(string ProviderId, string Id, string Name, int ContextLength, bool Vision)
{
    /// <summary>「供應商/模型」，存設定與比對用。</summary>
    public string Key => $"{ProviderId}/{Id}";
}
