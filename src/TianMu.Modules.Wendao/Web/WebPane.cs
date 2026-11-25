using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using TianMu.Modules.Wendao.Ai;
using TianMu.Modules.Wendao.Views;
using WebView = Microsoft.Web.WebView2.Wpf.WebView2;

namespace TianMu.Modules.Wendao.Web;

/// <summary>
/// 網頁模式：各家 AI 的官方網站直接嵌在問道裡，用你自己的帳號登入（登入會記住）。
/// 下方共用輸入框可以「自動挑選」一家、填進目前這一家，或同時填進所有開過的網站比較答案；
/// 只會幫你填字，送出由你在網頁上按，不替你操作別人的網站。
/// </summary>
internal sealed class WebPane : DockPanel
{
    private readonly string? _profileDir;
    private readonly Dictionary<string, WebView> _views = [];
    private readonly Dictionary<string, Button> _tabs = [];
    private readonly Grid _stage = new();
    private readonly TextBox _input = Look.Input(14);
    private readonly TextBlock _status = new() { Foreground = Look.Tertiary, FontSize = 12, Margin = new Thickness(2, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
    private CoreWebView2Environment? _env;
    private WebSite _current = WebSites.ChatGpt;

    public WebPane(string? dataDir)
    {
        _profileDir = dataDir is null ? null : Path.Combine(dataDir, "web-profile");

        // ── 上：各家分頁 ──
        var tabs = new WrapPanel { Margin = new Thickness(16, 10, 16, 8) };
        foreach (var site in WebSites.All)
        {
            if (site == WebSites.All.First(x => x.Video))
                tabs.Children.Add(new TextBlock { Text = "影片：", Foreground = Look.Tertiary, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 4) });
            var b = Look.Button(site.Name, () => _ = ShowAsync(site), tip: site.Strength);
            b.Margin = new Thickness(0, 0, 6, 4);
            _tabs[site.Id] = b;
            tabs.Children.Add(b);
        }
        var external = Look.Button("在瀏覽器開啟", () => OpenExternal(_current.Url), tip: "這一家的網頁改用你平常的瀏覽器開（例如內嵌畫面無法用 Google 登入時）");
        external.Margin = new Thickness(12, 0, 0, 4);
        tabs.Children.Add(external);
        var top = new Border { Child = tabs, BorderBrush = Look.Hairline, BorderThickness = new Thickness(0, 0, 0, 1) };
        SetDock(top, Dock.Top);
        Children.Add(top);

        // ── 下：共用輸入框 ──
        var composer = new DockPanel { Margin = new Thickness(16, 10, 16, 14) };
        var actions = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom };
        actions.Children.Add(Look.Button("自動挑選並填入", () => _ = AutoAsync(), primary: true, tip: "依問題性質選一家（程式→Claude、推理→DeepSeek、看圖或長文→Gemini、其他→ChatGPT），切過去並填入（Enter）"));
        actions.Children.Add(new Border { Height = 6 });
        actions.Children.Add(Look.Button("填入這一家", () => _ = FillAsync(_current, show: false)));
        actions.Children.Add(new Border { Height = 6 });
        actions.Children.Add(Look.Button("全部填入", () => _ = FillAllAsync(), tip: "填進所有開過的網站，方便比較答案"));
        SetDock(actions, Dock.Right);
        composer.Children.Add(actions);
        var stack = new StackPanel();
        _input.AcceptsReturn = true;
        _input.TextWrapping = TextWrapping.Wrap;
        _input.MinHeight = 56;
        _input.MaxHeight = 160;
        _input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { e.Handled = true; _ = AutoAsync(); }
        };
        stack.Children.Add(_input);
        _status.Text = "第一次使用請在各家網頁登入（會記住）。這裡只幫你把問題填進去，送出請在網頁上按。";
        stack.Children.Add(_status);
        composer.Children.Add(stack);
        var bottom = new Border { Child = composer, BorderBrush = Look.Hairline, BorderThickness = new Thickness(0, 1, 0, 0) };
        SetDock(bottom, Dock.Bottom);
        Children.Add(bottom);

        _stage.Background = Brushes.White;
        Children.Add(_stage);
        Loaded += (_, _) => { if (_views.Count == 0) _ = ShowAsync(_current); };
    }

    /// <summary>從 API 模式帶過來的問題。</summary>
    public string Draft { get => _input.Text; set => _input.Text = value; }

    private async Task<WebView?> EnsureAsync(WebSite site)
    {
        if (_views.TryGetValue(site.Id, out var existing)) return existing;
        try
        {
            _env ??= await CoreWebView2Environment.CreateAsync(null, _profileDir);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            _status.Text = "這台電腦缺少 Microsoft Edge WebView2，請先到微軟官網安裝後再試。";
            return null;
        }
        var view = new WebView { Visibility = Visibility.Collapsed };
        _stage.Children.Add(view);
        _views[site.Id] = view;
        await view.EnsureCoreWebView2Async(_env);
        view.CoreWebView2.Settings.AreDevToolsEnabled = false;
        view.CoreWebView2.Navigate(site.Url);
        return view;
    }

    private async Task ShowAsync(WebSite site)
    {
        _current = site;
        foreach (var (id, b) in _tabs)
        {
            bool on = id == site.Id;
            b.Background = on ? Look.Gold : Brushes.Transparent;
            b.Foreground = on ? Look.Lacquer : Look.Moon;
        }
        var view = await EnsureAsync(site);
        foreach (var (id, v) in _views) v.Visibility = id == site.Id ? Visibility.Visible : Visibility.Collapsed;
        view?.Focus();
    }

    private async Task AutoAsync()
    {
        string q = _input.Text.Trim();
        if (q.Length == 0) return;
        var kind = Router.Classify(q, hasImage: false);
        var site = WebSites.For(kind);
        await FillAsync(site, show: true);
        _status.Text = $"判斷為「{Router.Label(kind)}」→ {site.Name}。" + _status.Text;
    }

    private async Task FillAsync(WebSite site, bool show)
    {
        string q = _input.Text.Trim();
        if (q.Length == 0) return;
        if (show || site.Id == _current.Id) await ShowAsync(site);
        var view = await EnsureAsync(site);
        if (view?.CoreWebView2 is null) return;
        string result = "none";
        // 網頁可能還在載入：最多等 8 秒
        for (int i = 0; i < 16; i++)
        {
            try { result = (await view.CoreWebView2.ExecuteScriptAsync(WebSites.FillScript(q))).Trim('"'); }
            catch (Exception) { result = "none"; }
            if (result == "ok") break;
            await Task.Delay(500);
        }
        _status.Text = result == "ok"
            ? $"已填入 {site.Name}，請在網頁上按送出。"
            : $"{site.Name} 找不到輸入框：可能還沒登入、正在載入，或網站改版了。問題已複製，可以直接貼上。";
        if (result != "ok") try { Clipboard.SetText(q); } catch (Exception) { }
    }

    private async Task FillAllAsync()
    {
        if (_input.Text.Trim().Length == 0) return;
        var opened = _views.Keys.Select(WebSites.Find).OfType<WebSite>().Where(s => !s.Video).ToList();   // 影片網站不一起填
        if (opened.Count <= 1) { _status.Text = "先點上面幾家網站開起來，再按「全部填入」。"; return; }
        foreach (var site in opened) await FillAsync(site, show: false);
        _status.Text = $"已填入 {string.Join("、", opened.Select(s => s.Name))}，切換分頁到各家按送出即可比較。";
    }

    private static void OpenExternal(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception) { }
    }
}
