using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TianMu.Modules.Wendao.Ai;

/// <summary>生成的一張圖。</summary>
public sealed record ImageResult(byte[] Bytes, string Mime, string Source);

/// <summary>
/// 生成圖片：依序試 Cloudflare Workers AI（每天有免費額度，最可靠）→ Pollinations → Google Gemini；
/// 一家失敗或額度用完就換下一家。全部直接連各家，不經過中間平台。
/// </summary>
public sealed class ImageGenerator(Func<string, string?> keyOf) : IDisposable
{
    public const string CloudflareId = "cloudflare";

    /// <summary>Cloudflare 生圖需要「帳戶 ID」與「API 權杖」，設定頁讓你貼成「帳戶ID:權杖」。</summary>
    public static readonly Provider Cloudflare = new(CloudflareId, "Cloudflare Workers AI（生成圖片）", "https://api.cloudflare.com/client/v4/", true,
        "https://dash.cloudflare.com/profile/api-tokens",
        "生成圖片用（FLUX 模型），每天有免費額度。請貼成「帳戶 ID:API 權杖」——帳戶 ID 在 Cloudflare 首頁右側；權杖建立時選「Workers AI」範本。不設也可以，問道會先用免金鑰的 Pollinations。", 0.6);

    public const string PollinationsId = "pollinations";

    /// <summary>Pollinations 改版後原則上要金鑰（sk_ 開頭）；沒貼也會試一次。</summary>
    public static readonly Provider Pollinations = new(PollinationsId, "Pollinations（生成圖片，可不設）", "https://gen.pollinations.ai/", true,
        "https://enter.pollinations.ai/keys",
        "另一個生圖來源。改版後需要金鑰（sk_ 開頭，到網站登入後建立）；免費額度依網站規定。沒貼也會先試試看。", 0.5);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };

    public async Task<ImageResult> GenerateAsync(string prompt, int width, int height, Action<string> status, CancellationToken ct)
    {
        var errors = new List<string>();
        var attempts = new List<(string Name, Func<Task<ImageResult>> Run)>();
        bool hasCloudflare = SplitCloudflareKey(keyOf(CloudflareId)) is not null;
        if (SplitCloudflareKey(keyOf(CloudflareId)) is { } cf) attempts.Add(("Cloudflare", () => CloudflareAsync(prompt, cf.Account, cf.Token, ct)));
        attempts.Add(("Pollinations", () => PollinationsAsync(prompt, width, height, keyOf(PollinationsId), ct)));
        if (keyOf("gemini") is { Length: > 0 } gk) attempts.Add(("Google Gemini", () => GeminiAsync(prompt, width, height, gk, ct)));

        foreach (var (name, run) in attempts)
        {
            status($"{name} 正在畫…" + (errors.Count > 0 ? "（前一家失敗，已改用這家）" : ""));
            try { return await run(); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e) { errors.Add($"{name}：{Short(e.Message)}"); }
        }
        string hint = hasCloudflare ? "" :
            "\n\n建議設定 Cloudflare（免費，每天約可畫一兩百張）：左下角「連線設定」→ 最下面「Cloudflare Workers AI」→ 按「取得免費金鑰」，" +
            "建立權杖時選「Workers AI」範本，再把「帳戶 ID:權杖」貼回來。";
        throw new ProviderException("每一家都沒畫出來：\n• " + string.Join("\n• ", errors) + hint, null, false);
    }

    // ── Pollinations（新版 gen.pollinations.ai；金鑰放在 Authorization 標頭） ──

    public static string PollinationsUrl(string prompt, int width, int height, int seed) =>
        $"https://gen.pollinations.ai/image/{Uri.EscapeDataString(prompt)}?model=black-forest-labs/flux.1-schnell&width={width}&height={height}&seed={seed}";

    private async Task<ImageResult> PollinationsAsync(string prompt, int w, int h, string? key, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, PollinationsUrl(prompt, w, h, Random.Shared.Next(1, int.MaxValue)));
        if (!string.IsNullOrWhiteSpace(key)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
        using var res = await _http.SendAsync(req, ct);
        var bytes = await res.Content.ReadAsByteArrayAsync(ct);
        string mime = res.Content.Headers.ContentType?.MediaType ?? "";
        if (!res.IsSuccessStatusCode) throw new HttpRequestException((int)res.StatusCode switch
        {
            401 => string.IsNullOrWhiteSpace(key) ? "現在需要金鑰（可在連線設定貼上）" : "金鑰無效",
            402 => "額度不足",
            429 => "太頻繁了，稍等一下再畫",
            503 => "暫時無法使用",
            int c => $"回應錯誤（{c}）",
        });
        if (!mime.StartsWith("image/", StringComparison.Ordinal) || bytes.Length < 1000) throw new HttpRequestException("沒有回傳圖片");
        return new ImageResult(bytes, mime, "Pollinations");
    }

    // ── Cloudflare Workers AI：FLUX.1 schnell ──

    public static (string Account, string Token)? SplitCloudflareKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        int i = key.IndexOf(':');
        if (i <= 0 || i >= key.Length - 1) return null;
        return (key[..i].Trim(), key[(i + 1)..].Trim());
    }

    private async Task<ImageResult> CloudflareAsync(string prompt, string account, string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://api.cloudflare.com/client/v4/accounts/{Uri.EscapeDataString(account)}/ai/run/@cf/black-forest-labs/flux-1-schnell")
        {
            Content = new StringContent(new JsonObject { ["prompt"] = prompt, ["steps"] = 6 }.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await _http.SendAsync(req, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException((int)res.StatusCode is 401 or 403 ? "帳戶 ID 或權杖不對" : (int)res.StatusCode == 429 ? "今天的免費額度用完了" : $"回應錯誤（{(int)res.StatusCode}）");
        return new ImageResult(ParseCloudflare(body), "image/jpeg", "Cloudflare FLUX");
    }

    public static byte[] ParseCloudflare(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("result", out var r) && r.TryGetProperty("image", out var img) && img.GetString() is { Length: > 0 } b64)
            return Convert.FromBase64String(b64);
        throw new HttpRequestException("沒有回傳圖片");
    }

    // ── Google Gemini：用 Gemini 金鑰（免費額度可能很少或沒有） ──

    private async Task<ImageResult> GeminiAsync(string prompt, int w, int h, string key, CancellationToken ct)
    {
        string ratio = AspectName(w, h);
        var body = new JsonObject
        {
            ["contents"] = new JsonArray { new JsonObject { ["parts"] = new JsonArray { new JsonObject { ["text"] = prompt } } } },
            ["generationConfig"] = new JsonObject
            {
                ["responseModalities"] = new JsonArray { "TEXT", "IMAGE" },
                ["imageConfig"] = new JsonObject { ["aspectRatio"] = ratio },
            },
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-image:generateContent")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("x-goog-api-key", key.Trim());
        using var res = await _http.SendAsync(req, ct);
        string text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException((int)res.StatusCode == 429 ? "免費額度用完或沒有生圖額度" : $"回應錯誤（{(int)res.StatusCode}）");
        var (bytes, mime) = ParseGemini(text);
        return new ImageResult(bytes, mime, "Google Gemini");
    }

    public static (byte[] Bytes, string Mime) ParseGemini(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("candidates", out var cands))
            foreach (var c in cands.EnumerateArray())
                if (c.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
                    foreach (var p in parts.EnumerateArray())
                        if ((p.TryGetProperty("inlineData", out var data) || p.TryGetProperty("inline_data", out data)) && data.TryGetProperty("data", out var b64))
                        {
                            string mime = data.TryGetProperty("mimeType", out var m) ? m.GetString() ?? "image/png" : data.TryGetProperty("mime_type", out var m2) ? m2.GetString() ?? "image/png" : "image/png";
                            return (Convert.FromBase64String(b64.GetString()!), mime);
                        }
        throw new HttpRequestException("沒有回傳圖片");
    }

    /// <summary>圖片比例：方形、橫式 16:9、直式 9:16。</summary>
    public static (int Width, int Height) SizeOf(string aspect) => aspect switch
    {
        "16:9" => (1344, 768),
        "9:16" => (768, 1344),
        "4:3" => (1152, 864),
        _ => (1024, 1024),
    };

    public static string AspectName(int w, int h) => w == h ? "1:1" : w > h ? (Math.Abs((double)w / h - 4.0 / 3) < 0.05 ? "4:3" : "16:9") : "9:16";

    private static string Short(string s) => s.Length > 120 ? s[..120] + "…" : s;

    public void Dispose() => _http.Dispose();
}
