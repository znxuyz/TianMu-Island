using System.IO;
using System.Text.Json;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;

namespace TianMu.Modules.Ai.Claude;

/// <summary>網頁查詢的結果。</summary>
/// <param name="Status">HTTP 狀態碼（0 = 網路錯誤）。</param>
/// <param name="Body">回應內容。</param>
/// <param name="Plan">從組織資訊推得的方案（Max、Pro…）。</param>
internal sealed record WebFetchResult(int Status, string Body, string? Plan);

/// <summary>
/// 天幕島自己的 claude.ai 登入（內嵌 Edge WebView2）。
///
/// 使用者在天幕島的登入視窗裡登入 claude.ai 一次，登入狀態存在天幕島專屬的資料夾；
/// 查詢額度時在一個看不見的 claude.ai 頁面裡用網站本身的方式呼叫額度介面，
/// 所以不需要複製任何金鑰，也不會被網站的安全驗證擋下。
/// 所有成員都必須在 UI 執行緒呼叫。
/// </summary>
internal sealed class ClaudeWebSession(string userDataFolder) : IDisposable
{
    public const string LoginUrl = "https://claude.ai/login";
    private const string Origin = "https://claude.ai";
    private const string BlankPage = "https://claude.ai/robots.txt"; // 輕量的同源頁面，只用來執行查詢

    private CoreWebView2Environment? _environment;
    private HwndSource? _host;
    private CoreWebView2Controller? _controller;
    private readonly Dictionary<string, TaskCompletionSource<WebFetchResult>> _pending = [];

    /// <summary>登入視窗與背景查詢共用同一個瀏覽器環境（同一份登入狀態）。</summary>
    public async Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        if (_environment is not null) return _environment;
        Directory.CreateDirectory(userDataFolder);
        try
        {
            _environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            throw new UsageException("這台電腦缺少 Microsoft Edge WebView2，請先到微軟官網安裝後再試。");
        }
        return _environment;
    }

    private string MarkerPath => Path.Combine(userDataFolder, "logged-in");

    /// <summary>
    /// 是否曾經登入過（不必啟動瀏覽器就能判斷）。從沒登入過的使用者完全不會啟動內嵌瀏覽器，省下記憶體。
    /// </summary>
    public bool MayBeLoggedIn => File.Exists(MarkerPath);

    /// <summary>登入視窗偵測到登入成功時呼叫。</summary>
    public void MarkLoggedIn()
    {
        Directory.CreateDirectory(userDataFolder);
        File.WriteAllText(MarkerPath, DateTimeOffset.Now.ToString("O"));
    }

    /// <summary>是否已在天幕島裡登入 claude.ai。</summary>
    public async Task<bool> IsLoggedInAsync()
    {
        if (!MayBeLoggedIn) return false;
        var core = await GetCoreAsync();
        var cookies = await core.CookieManager.GetCookiesAsync(Origin);
        return cookies.Any(c => c.Name == "sessionKey" && !string.IsNullOrEmpty(c.Value));
    }

    /// <summary>登出：清除天幕島裡的 claude.ai 登入。</summary>
    public async Task LogOutAsync()
    {
        var core = await GetCoreAsync();
        core.CookieManager.DeleteAllCookies();
        if (File.Exists(MarkerPath)) File.Delete(MarkerPath);
        ReleaseBrowser();
    }

    /// <summary>在 claude.ai 頁面裡查詢額度（找出訂閱所在的組織，再取它的額度）。</summary>
    public async Task<WebFetchResult> FetchUsageAsync(CancellationToken ct)
    {
        var core = await GetCoreAsync();
        string id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<WebFetchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        string script = $$"""
            (async () => {
              const send = (status, body, plan) => window.chrome.webview.postMessage(JSON.stringify({ id: "{{id}}", status, body, plan }));
              try {
                const orgsResponse = await fetch("/api/organizations", { credentials: "include" });
                if (!orgsResponse.ok) { send(orgsResponse.status, await orgsResponse.text(), null); return; }
                const orgs = await orgsResponse.json();
                const org = orgs.find(o => (o.capabilities || []).includes("chat")) || orgs[0];
                if (!org) { send(404, "no organization", null); return; }
                const caps = org.capabilities || [];
                const plan = caps.includes("claude_max") ? "max" : caps.includes("claude_pro") ? "pro" : (org.raven_type || null);
                const usage = await fetch("/api/organizations/" + org.uuid + "/usage", { credentials: "include" });
                send(usage.status, await usage.text(), plan);
              } catch (e) {
                send(0, String(e), null);
              }
            })();
            """;

        try
        {
            await core.ExecuteScriptAsync(script);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using (timeout.Token.Register(() => tcs.TrySetException(new UsageException("claude.ai 回應逾時，稍後會再試。"))))
                return await tcs.Task;
        }
        finally
        {
            _pending.Remove(id);
            ReleaseBrowser(); // 查完就關掉背景瀏覽器，下次（5 分鐘後）再開
        }
    }

    /// <summary>關閉背景瀏覽器，釋放記憶體（登入狀態保留在資料夾裡）。</summary>
    private void ReleaseBrowser()
    {
        if (_controller is null) return;
        _controller.CoreWebView2.WebMessageReceived -= OnMessage;
        _controller.Close();
        _controller = null;
    }

    private async Task<CoreWebView2> GetCoreAsync()
    {
        if (_controller is not null) return _controller.CoreWebView2;

        var env = await GetEnvironmentAsync();

        // 一個永遠不顯示的小視窗，承載背景查詢用的瀏覽器
        _host ??= new HwndSource(new HwndSourceParameters("TianMu.ClaudeSession")
        {
            Width = 1,
            Height = 1,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP，不顯示
        });

        _controller = await env.CreateCoreWebView2ControllerAsync(_host.Handle);
        _controller.IsVisible = false;
        var core = _controller.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.WebMessageReceived += OnMessage;

        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnLoaded(object? s, CoreWebView2NavigationCompletedEventArgs e) => loaded.TrySetResult();
        core.NavigationCompleted += OnLoaded;
        core.Navigate(BlankPage);
        await Task.WhenAny(loaded.Task, Task.Delay(TimeSpan.FromSeconds(20)));
        core.NavigationCompleted -= OnLoaded;

        return core;
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
            var root = doc.RootElement;
            string id = root.GetProperty("id").GetString() ?? "";
            if (!_pending.TryGetValue(id, out var tcs)) return;

            int status = root.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0;
            string body = root.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() ?? "" : "";
            string? plan = root.TryGetProperty("plan", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            tcs.TrySetResult(new WebFetchResult(status, body, plan));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // 不是我們的訊息
        }
    }

    public void Dispose()
    {
        _controller?.Close();
        _controller = null;
        _host?.Dispose();
        _host = null;
    }
}
