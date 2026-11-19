using System.Globalization;
using System.Text.Json;

namespace TianMu.Modules.Wendao.Ai;

/// <summary>把各家 /models 的回應整理成「能聊天、而且免費」的模型清單（純邏輯，方便測試）。</summary>
public static class ModelCatalog
{
    // 不是拿來聊天的模型：語音、向量、審查、生圖⋯
    private static readonly string[] NotChat =
        ["embed", "whisper", "tts", "audio", "guard", "moderation", "imagen", "veo", "image-generation", "-image", "aqa", "live", "transcribe", "ocr", "learnlm", "search"];

    public static List<ModelInfo> Parse(string providerId, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var list = root.ValueKind == JsonValueKind.Array ? root : root.TryGetProperty("data", out var d) ? d : root.TryGetProperty("models", out var m) ? m : default;
        if (list.ValueKind != JsonValueKind.Array) return [];

        var result = new List<ModelInfo>();
        foreach (var item in list.EnumerateArray())
        {
            string? id = Str(item, "id") ?? Str(item, "name");
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (id.StartsWith("models/", StringComparison.Ordinal)) id = id["models/".Length..];
            string lower = id.ToLowerInvariant();
            if (NotChat.Any(lower.Contains)) continue;

            int ctx = Int(item, "context_length") ?? Int(item, "context_window") ?? Int(item, "max_context_length") ?? Int(item, "inputTokenLimit") ?? 0;
            bool vision = HasVision(item, lower);
            string name = Str(item, "name") is { } n && !n.StartsWith("models/", StringComparison.Ordinal) && n != id ? n : Pretty(id);

            switch (providerId)
            {
                case "openrouter":
                    // 只收免費的：名稱帶 :free，或價格明確為 0
                    if (!IsFree(item, id)) continue;
                    break;
                case "gemini":
                    if (!lower.StartsWith("gemini", StringComparison.Ordinal) && !lower.StartsWith("gemma", StringComparison.Ordinal)) continue;
                    if (lower.Contains("gemini") && ctx == 0) ctx = 1_000_000;
                    vision = vision || lower.StartsWith("gemini", StringComparison.Ordinal);
                    break;
                case "mistral":
                    if (item.TryGetProperty("capabilities", out var caps) && caps.TryGetProperty("completion_chat", out var chat) && chat.ValueKind == JsonValueKind.False) continue;
                    break;
            }
            result.Add(new ModelInfo(providerId, id, name, ctx, vision));
        }
        return result.GroupBy(x => x.Id).Select(g => g.First()).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsFree(JsonElement item, string id)
    {
        if (id.EndsWith(":free", StringComparison.Ordinal)) return true;
        if (!item.TryGetProperty("pricing", out var p)) return false;
        return Zero(p, "prompt") && Zero(p, "completion");
    }

    private static bool Zero(JsonElement p, string name) =>
        p.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.String
            ? decimal.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d == 0
            : v.ValueKind == JsonValueKind.Number && v.GetDecimal() == 0);

    private static bool HasVision(JsonElement item, string lower)
    {
        if (item.TryGetProperty("architecture", out var arch) && arch.TryGetProperty("input_modalities", out var mods) && mods.ValueKind == JsonValueKind.Array)
            return mods.EnumerateArray().Any(x => x.GetString() == "image");
        if (item.TryGetProperty("capabilities", out var caps) && caps.TryGetProperty("vision", out var v) && v.ValueKind == JsonValueKind.True) return true;
        return lower.Contains("vision") || lower.Contains("llava") || lower.Contains("-vl") || lower.Contains("llama-4") || lower.Contains("pixtral") || lower.Contains("gemma3");
    }

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? (int)Math.Min(n, int.MaxValue) : null;

    /// <summary>「meta-llama/llama-3.3-70b-instruct:free」→「llama-3.3-70b-instruct」。</summary>
    public static string Pretty(string id)
    {
        string s = id;
        int slash = s.LastIndexOf('/');
        if (slash >= 0) s = s[(slash + 1)..];
        if (s.EndsWith(":free", StringComparison.Ordinal)) s = s[..^5];
        return s;
    }
}
