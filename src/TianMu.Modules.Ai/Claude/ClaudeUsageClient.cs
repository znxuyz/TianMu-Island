using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace TianMu.Modules.Ai.Claude;

/// <summary>查詢失敗，<see cref="Exception.Message"/> 是可以直接給使用者看的說明。</summary>
public sealed class UsageException(string message) : Exception(message);

/// <summary>額度來源。</summary>
public enum UsageSource
{
    None,
    /// <summary>Claude Code 的登入（自動偵測）。</summary>
    ClaudeCode,
    /// <summary>在天幕島裡登入的 claude.ai。</summary>
    Web,
}

/// <summary>
/// 用 Claude Code 的登入權杖向 Anthropic 查詢 Claude 訂閱額度（Claude Code 內部使用的介面，非公開正式 API）。
/// 權杖只會送到 api.anthropic.com。網頁登入的查詢見 <see cref="ClaudeWebSession"/>。
/// </summary>
internal sealed class ClaudeUsageClient : IDisposable
{
    private const string OAuthUsageUrl = "https://api.anthropic.com/api/oauth/usage";

    private readonly HttpClient _http;

    public ClaudeUsageClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) TianMu-Island");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<ClaudeUsage> FetchWithClaudeCodeAsync(ClaudeCodeLogin login, CancellationToken ct)
    {
        if (login.IsExpired(DateTimeOffset.Now))
            throw new UsageException("Claude Code 的登入已過期：開啟 Claude Code 一次，它會自動更新，天幕島隨後就讀得到。");

        using var request = new HttpRequestMessage(HttpMethod.Get, OAuthUsageUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        string body = await SendAsync(request, "Anthropic", ct);
        return ClaudeUsageParser.Parse(body, DateTimeOffset.Now);
    }

    private async Task<string> SendAsync(HttpRequestMessage request, string service, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException)
        {
            throw new UsageException($"連不到 {service}，請檢查網路。");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new UsageException($"{service} 回應逾時，稍後會再試。");
        }

        using (response)
        {
            string body = await response.Content.ReadAsStringAsync(ct);
            bool isJson = response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;

            ThrowIfFailed((int)response.StatusCode, isJson, service);
            return body;
        }
    }

    /// <summary>把失敗的狀態碼轉成給使用者看的說明（網頁登入與 Claude Code 共用）。</summary>
    internal static void ThrowIfFailed(int status, bool isJson, string service)
    {
        switch (status)
        {
            case >= 200 and < 300 when isJson:
                return;
            case >= 200 and < 300:
                throw new UsageException($"{service} 回應的不是額度資料，介面可能已變更，需要更新天幕島。");
            case 0:
                throw new UsageException($"連不到 {service}，請檢查網路。");
            case 401 or 403:
                throw new UsageException($"{service} 的登入已失效，請按「連結」重新登入。");
            case 404:
                throw new UsageException($"{service} 找不到額度資料，介面可能已變更，需要更新天幕島。");
            case 429:
                throw new UsageException("查詢太頻繁，稍後會再試。");
            default:
                throw new UsageException($"{service} 回應錯誤（{status}），稍後會再試。");
        }
    }

    public void Dispose() => _http.Dispose();
}
