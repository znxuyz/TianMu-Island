using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TianMu.Modules.Ai.Claude;

/// <summary>查詢失敗，<see cref="Exception.Message"/> 是可以直接給使用者看的說明。</summary>
public sealed class UsageException(string message) : Exception(message);

/// <summary>額度來源。</summary>
public enum UsageSource
{
    None,
    /// <summary>Claude Code 的登入（自動偵測）。</summary>
    ClaudeCode,
    /// <summary>使用者貼上的 claude.ai sessionKey。</summary>
    SessionKey,
}

/// <summary>
/// 向 Anthropic 查詢 Claude 訂閱額度。兩種來源（優先順序由上而下）：
///   1. 使用者貼上的 claude.ai sessionKey → claude.ai 網頁用的額度介面
///   2. Claude Code 的登入權杖 → Claude Code 用的額度介面
/// 兩者都不是公開的正式 API，Anthropic 隨時可能調整；失敗時會回報明確原因。
/// 權杖與 sessionKey 只會送到 anthropic.com / claude.ai，不會送到任何其他地方。
/// </summary>
internal sealed class ClaudeUsageClient : IDisposable
{
    private const string OAuthUsageUrl = "https://api.anthropic.com/api/oauth/usage";
    private const string WebOrganizationsUrl = "https://claude.ai/api/organizations";

    private readonly HttpClient _http;
    private string? _cachedOrgId;

    public ClaudeUsageClient()
    {
        _http = new HttpClient(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) TianMu-Island");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<ClaudeUsage> FetchWithSessionKeyAsync(string sessionKey, CancellationToken ct)
    {
        string orgId = _cachedOrgId ?? await FindOrganizationAsync(sessionKey, ct);
        _cachedOrgId = orgId;

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{WebOrganizationsUrl}/{Uri.EscapeDataString(orgId)}/usage");
        request.Headers.Add("Cookie", $"sessionKey={sessionKey}");
        string body = await SendAsync(request, "claude.ai", ct);
        return ClaudeUsageParser.Parse(body, DateTimeOffset.Now);
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

    public void ForgetOrganization() => _cachedOrgId = null;

    private async Task<string> FindOrganizationAsync(string sessionKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, WebOrganizationsUrl);
        request.Headers.Add("Cookie", $"sessionKey={sessionKey}");
        string body = await SendAsync(request, "claude.ai", ct);

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new UsageException("claude.ai 回傳的組織清單格式已變更，暫時無法查詢。");

        string? fallback = null;
        foreach (var org in doc.RootElement.EnumerateArray())
        {
            if (!org.TryGetProperty("uuid", out var id) || id.GetString() is not { Length: > 0 } uuid) continue;
            fallback ??= uuid;

            // 優先選有「聊天」能力的個人組織（也就是 Claude 訂閱所在的組織）
            if (org.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array &&
                caps.EnumerateArray().Any(c => c.GetString() == "chat"))
                return uuid;
        }

        return fallback ?? throw new UsageException("這個 claude.ai 帳號底下找不到任何組織。");
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

            if (response.StatusCode is HttpStatusCode.Unauthorized)
                throw new UsageException($"{service} 的登入已失效，請重新連結。");
            if (response.StatusCode is HttpStatusCode.Forbidden && !isJson)
                throw new UsageException($"{service} 擋下了這次查詢（可能是安全驗證），請稍後再試或改用 Claude Code 的登入。");
            if (response.StatusCode is HttpStatusCode.Forbidden)
                throw new UsageException($"{service} 拒絕了這次查詢，請重新連結。");
            if (response.StatusCode is HttpStatusCode.TooManyRequests)
                throw new UsageException("查詢太頻繁，稍後會再試。");
            if (!response.IsSuccessStatusCode)
                throw new UsageException($"{service} 回應錯誤（{(int)response.StatusCode}），稍後會再試。");
            if (!isJson)
                throw new UsageException($"{service} 回應的不是額度資料，介面可能已變更。");

            return body;
        }
    }

    public void Dispose() => _http.Dispose();
}
