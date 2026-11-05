using System.Windows;
using System.Windows.Threading;
using TianMu.Modules.Ai.Claude;

namespace TianMu.Modules.Ai.Views;

/// <summary>
/// 在天幕島裡登入 claude.ai 的視窗（內嵌 Edge WebView2）。
/// 偵測到登入完成（出現登入 Cookie 且離開登入頁）就自動關閉並通知呼叫端。
/// </summary>
public partial class LoginWindow : Window
{
    private readonly ClaudeWebSession _session;
    private readonly Action _loggedIn;
    private readonly DispatcherTimer _poll;
    private bool _done;

    internal LoginWindow(ClaudeWebSession session, Action loggedIn)
    {
        InitializeComponent();
        _session = session;
        _loggedIn = loggedIn;

        _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _poll.Tick += async (_, _) => await CheckLoggedInAsync();

        Loaded += async (_, _) => await StartAsync();
        Closed += (_, _) => _poll.Stop();
    }

    private async Task StartAsync()
    {
        try
        {
            await Browser.EnsureCoreWebView2Async(await _session.GetEnvironmentAsync());
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Browser.CoreWebView2.Navigate(ClaudeWebSession.LoginUrl);
            _poll.Start();
        }
        catch (UsageException ex)
        {
            Hint.Text = ex.Message;
        }
        catch (Exception ex)
        {
            Hint.Text = $"無法開啟登入頁：{ex.Message}";
        }
    }

    private async Task CheckLoggedInAsync()
    {
        if (_done || Browser.CoreWebView2 is null) return;

        var cookies = await Browser.CoreWebView2.CookieManager.GetCookiesAsync("https://claude.ai");
        bool hasSession = cookies.Any(c => c.Name == "sessionKey" && !string.IsNullOrEmpty(c.Value));
        bool onLoginPage = Browser.Source?.AbsolutePath.StartsWith("/login", StringComparison.OrdinalIgnoreCase) == true ||
                           Browser.Source?.AbsolutePath.StartsWith("/magic-link", StringComparison.OrdinalIgnoreCase) == true;

        if (!hasSession || onLoginPage) return;

        _done = true;
        _poll.Stop();
        _session.MarkLoggedIn();
        _loggedIn();
        Close();
    }
}
