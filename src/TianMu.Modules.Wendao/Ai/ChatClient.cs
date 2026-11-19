using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO;

namespace TianMu.Modules.Wendao.Ai;

public enum Role { System, User, Assistant }

/// <summary>一則訊息；可以附一張圖（PNG／JPEG 位元組）。</summary>
public sealed record ChatMessage(Role Role, string Text, byte[]? Image = null, string ImageMime = "image/png");

/// <summary>呼叫失敗。<see cref="TryNext"/> 為 true 表示換一家可能就好（額度用完、暫時故障、模型下架）。</summary>
public sealed class ProviderException(string message, HttpStatusCode? status, bool tryNext) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
    public bool TryNext { get; } = tryNext;
}

/// <summary>相容 OpenAI 的對話介面：列模型、串流回答。各家共用這一份。</summary>
public sealed class ChatClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public ChatClient()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("TianMuIsland-Wendao/1.0");
        // OpenRouter 用來標示來源（不含任何個人資料）
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Title", "TianMu Island");
    }

    public async Task<List<ModelInfo>> ListModelsAsync(Provider p, string? key, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, p.BaseUrl + "models");
        Authorize(req, key);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));
        HttpResponseMessage res;
        try { res = await _http.SendAsync(req, cts.Token).ConfigureAwait(false); }
        catch (HttpRequestException e) { throw new ProviderException(p.Id == "ollama" ? "連不到本機的 Ollama（還沒安裝或沒有啟動）。" : $"連不上 {p.Name}：{e.Message}", null, true); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new ProviderException($"{p.Name} 沒有回應。", null, true); }
        using (res)
        {
            string body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) throw Error(p, res.StatusCode, body);
            return ModelCatalog.Parse(p.Id, body);
        }
    }

    /// <summary>串流回答：一段一段把字吐出來。</summary>
    public async IAsyncEnumerable<string> StreamAsync(Provider p, string? key, string model, IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, p.BaseUrl + "chat/completions")
        {
            Content = new StringContent(BuildRequest(model, messages), Encoding.UTF8, "application/json"),
        };
        Authorize(req, key);
        HttpResponseMessage res;
        try { res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false); }
        catch (HttpRequestException e) { throw new ProviderException($"連不上 {p.Name}：{e.Message}", null, true); }
        using (res)
        {
            if (!res.IsSuccessStatusCode)
                throw Error(p, res.StatusCode, await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));

            using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            bool any = false;
            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                if (ParseSseLine(line, out string? piece, out string? error, out bool done))
                {
                    if (error is not null) throw new ProviderException($"{p.Name}：{error}", null, !any);
                    if (done) yield break;
                    if (!string.IsNullOrEmpty(piece)) { any = true; yield return piece; }
                }
            }
            if (!any) throw new ProviderException($"{p.Name} 沒有回任何內容。", null, true);
        }
    }

    private static void Authorize(HttpRequestMessage req, string? key)
    {
        if (!string.IsNullOrWhiteSpace(key)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
    }

    /// <summary>組成請求內容（有圖片就用 image_url 的 data URL）。</summary>
    public static string BuildRequest(string model, IReadOnlyList<ChatMessage> messages)
    {
        var arr = new JsonArray();
        foreach (var m in messages)
        {
            string role = m.Role switch { Role.System => "system", Role.Assistant => "assistant", _ => "user" };
            JsonNode content;
            if (m.Image is { Length: > 0 } img)
            {
                content = new JsonArray
                {
                    new JsonObject { ["type"] = "text", ["text"] = m.Text },
                    new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = $"data:{m.ImageMime};base64,{Convert.ToBase64String(img)}" } },
                };
            }
            else content = JsonValue.Create(m.Text)!;
            arr.Add(new JsonObject { ["role"] = role, ["content"] = content });
        }
        return new JsonObject { ["model"] = model, ["messages"] = arr, ["stream"] = true }.ToJsonString();
    }

    /// <summary>解析一行 SSE：「data: {...}」。回傳 false 表示這行不用理（空行、註解、keep-alive）。</summary>
    public static bool ParseSseLine(string line, out string? piece, out string? error, out bool done)
    {
        piece = error = null;
        done = false;
        if (!line.StartsWith("data:", StringComparison.Ordinal)) return false;
        string data = line[5..].Trim();
        if (data == "[DONE]") { done = true; return true; }
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err))
            {
                error = err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var msg) ? msg.GetString() : err.ToString();
                return true;
            }
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var c = choices[0];
                if (c.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var text) && text.ValueKind == JsonValueKind.String)
                    piece = text.GetString();
                else if (c.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var full) && full.ValueKind == JsonValueKind.String)
                    piece = full.GetString();
            }
            return true;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>把錯誤碼翻成白話，並判斷要不要換一家試。</summary>
    public static ProviderException Error(Provider p, HttpStatusCode code, string body)
    {
        string detail = ExtractMessage(body);
        return (int)code switch
        {
            401 or 403 => new ProviderException($"{p.Name} 的金鑰無效或沒有權限，請到「連線」重新貼一次。{detail}", code, true),
            402 => new ProviderException($"{p.Name}：這個模型需要付費額度。{detail}", code, true),
            404 => new ProviderException($"{p.Name}：找不到這個模型（可能已下架）。{detail}", code, true),
            413 => new ProviderException($"{p.Name}：內容太長，超過這個模型能讀的長度。", code, true),
            429 => new ProviderException($"{p.Name} 的免費額度暫時用完了。{detail}", code, true),
            >= 500 => new ProviderException($"{p.Name} 暫時故障（{(int)code}）。", code, true),
            _ => new ProviderException($"{p.Name} 回應錯誤（{(int)code}）。{detail}", code, (int)code is 400 or 422),
        };
    }

    private static string ExtractMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0 ? doc.RootElement[0] : doc.RootElement;
            if (root.TryGetProperty("error", out var e))
                return e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var m) ? Trim(m.GetString()) : Trim(e.ToString());
            if (root.TryGetProperty("message", out var msg)) return Trim(msg.GetString());
        }
        catch (JsonException) { }
        return "";
    }

    private static string Trim(string? s) => string.IsNullOrWhiteSpace(s) ? "" : "\n" + (s.Length > 200 ? s[..200] + "…" : s);

    public void Dispose() => _http.Dispose();
}
