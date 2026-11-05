using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using TianMu.Core;
using TianMu.Modules.Notify.Notifications;
using TianMu.Modules.Notify.Views;

namespace TianMu.Modules.Notify;

/// <summary>
/// 法器「傳音入密」：
///   · 系統通知（LINE、Outlook、瀏覽器、手機連結…）以符紙形式從天幕滑出
///   · 通知裡的驗證碼自動辨識，大字顯示、點一下就複製；60 秒後自動從剪貼簿清除
///   · 天幕常駐一頁「傳音」：最新驗證碼與最近三則通知
/// 通知內容只留在記憶體裡，不存檔、不上傳。
/// </summary>
public sealed class NotifyModule : IIslandModule, IDescribedModule
{
    public const string ShowAllOption = "showAll";
    public const string OtpOption = "otp";
    public const string FocusQuietOption = "focusQuiet";

    private const int InboxSize = 3;
    private static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OtpDuration = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan OtpFreshFor = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ClipboardLifetime = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(5);

    private readonly NotificationDbWatcher _watcher = new();
    private readonly List<ToastNotice> _recent = [];
    private IIslandHost? _host;

    // 以下只在 UI 執行緒存取
    private NoticeViewModel? _noticeVm;
    private NoticeView? _noticeView;
    private OtpViewModel? _otpVm;
    private OtpView? _otpView;
    private InboxViewModel? _inboxVm;
    private InboxView? _inboxView;
    private DispatcherTimer? _clipboardTimer;
    private DispatcherTimer? _freshTimer;
    private string? _copiedCode;
    private DateTime _otpAt;
    private (string Key, DateTime At) _last;

    public string Id => "tianmu.notify";
    public string DisplayName => "傳音入密";
    public string Description => "系統通知從天幕滑出；通知裡的驗證碼自動辨識，點一下就複製。";

    public IReadOnlyList<ModuleOption> Options { get; } =
    [
        new(ShowAllOption, "顯示所有通知", "每則新通知都從天幕滑出（關閉後只記在「傳音」頁）。", true),
        new(OtpOption, "驗證碼一鍵複製", "辨識通知裡的驗證碼，大字顯示並可一鍵複製。", true),
        new(FocusQuietOption, "專注時只顯示驗證碼", "番茄鐘進行時，一般通知不打擾，驗證碼照常顯示。", true),
    ];

    public async Task InitializeAsync(IIslandHost host, CancellationToken cancellationToken)
    {
        _host = host;

        await host.InvokeAsync(() =>
        {
            _noticeVm = new NoticeViewModel();
            _noticeView = new NoticeView { DataContext = _noticeVm };

            var copy = new RelayCommand(CopyLatestCode);
            _otpVm = new OtpViewModel { CopyCommand = copy };
            _otpView = new OtpView { DataContext = _otpVm };
            _inboxVm = new InboxViewModel { Otp = new OtpViewModel { CopyCommand = copy } };
            _inboxView = new InboxView { DataContext = _inboxVm };

            _clipboardTimer = new DispatcherTimer { Interval = ClipboardLifetime };
            _clipboardTimer.Tick += (_, _) => ClearCopiedCode();

            // 驗證碼過了 10 分鐘就從頁面上撤下
            _freshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _freshTimer.Tick += (_, _) => RefreshInbox();
            _freshTimer.Start();

            RefreshInbox();
        });

        host.SetPage(new IslandActivity(Id, DisplayName)
        {
            ExpandedView = _inboxView,
        });

        _watcher.Arrived += notice => _ = host.InvokeAsync(() => OnNotice(notice));
        _watcher.Failed += message => _ = host.InvokeAsync(() =>
            host.Post(new IslandActivity(Id, "無法讀取系統通知", "這個 Windows 版本的通知紀錄格式不同，傳音入密暫時無法使用",
                ActivityPriority.Normal, TimeSpan.FromSeconds(6))));
        _watcher.Start();
    }

    // ═══════════════════════ 收到通知 ═══════════════════════

    private void OnNotice(ToastNotice notice)
    {
        if (_host is null || _noticeVm is null || _otpVm is null) return;

        // 有些程式會一次送出兩則一模一樣的通知
        string key = $"{notice.AppId}\u0001{notice.Title}\u0001{notice.Body}";
        if (key == _last.Key && DateTime.Now - _last.At < DuplicateWindow) return;
        _last = (key, DateTime.Now);

        _recent.Insert(0, notice);
        if (_recent.Count > 20) _recent.RemoveAt(_recent.Count - 1);

        var settings = _host.GetModuleSettings(Id);
        string source = SourceName(notice);

        // 驗證碼優先：使用者正在等它
        string? code = settings.GetBool(OtpOption, true) ? OtpExtractor.Find($"{notice.Title}\n{notice.Body}") : null;
        if (code is not null)
        {
            _otpAt = DateTime.Now;
            FillOtp(_otpVm, code, $"{source} · {_otpAt:HH:mm}");
            RefreshInbox();
            _host.Post(new IslandActivity(Id, "驗證碼", OtpExtractor.Pretty(code), ActivityPriority.Alert, OtpDuration)
            {
                ExpandedView = _otpView,
            });
            return;
        }

        RefreshInbox();

        if (!settings.GetBool(ShowAllOption, true)) return;
        if (_host.IsFocusMode && settings.GetBool(FocusQuietOption, true)) return;

        _noticeVm.Title = notice.Title;
        _noticeVm.Body = notice.Body;
        _noticeVm.Source = $"{source} · 剛剛";
        _host.Post(new IslandActivity(Id, notice.Title, notice.Body, ActivityPriority.Normal, NoticeDuration)
        {
            ExpandedView = _noticeView,
        });
    }

    /// <summary>瀏覽器通知用網站名稱（例如 mail.google.com），其餘用 App 名稱。</summary>
    private static string SourceName(ToastNotice notice) =>
        string.IsNullOrWhiteSpace(notice.Attribution) ? notice.AppName : notice.Attribution!;

    private static void FillOtp(OtpViewModel vm, string code, string source)
    {
        vm.Code = code;
        vm.PrettyCode = OtpExtractor.Pretty(code);
        vm.Source = source;
        vm.Action = "點擊複製";
        vm.HasCode = true;
    }

    // ═══════════════════════ 驗證碼複製 ═══════════════════════

    private void CopyLatestCode()
    {
        if (_otpVm is null || string.IsNullOrEmpty(_otpVm.Code)) return;
        string code = _otpVm.Code;

        try
        {
            Clipboard.SetText(code);
        }
        catch (Exception ex)
        {
            // 剪貼簿偶爾被其他程式佔用
            Debug.WriteLine($"[傳音入密] 複製失敗：{ex.Message}");
            _otpVm.Action = "複製失敗，再點一次";
            return;
        }

        _copiedCode = code;
        _otpVm.Action = "已複製";
        if (_inboxVm is not null) _inboxVm.Otp.Action = "已複製";
        _clipboardTimer?.Stop();
        _clipboardTimer?.Start();
    }

    /// <summary>60 秒後，若剪貼簿裡還是這組驗證碼就清掉，避免被其他程式讀走。</summary>
    private void ClearCopiedCode()
    {
        _clipboardTimer?.Stop();
        if (_copiedCode is null) return;
        try
        {
            if (Clipboard.ContainsText() && Clipboard.GetText() == _copiedCode) Clipboard.Clear();
        }
        catch { /* 剪貼簿被佔用就算了 */ }
        _copiedCode = null;
    }

    // ═══════════════════════ 傳音頁 ═══════════════════════

    private void RefreshInbox()
    {
        if (_inboxVm is null || _otpVm is null) return;

        bool fresh = _otpVm.HasCode && DateTime.Now - _otpAt < OtpFreshFor;
        if (fresh)
        {
            FillOtp(_inboxVm.Otp, _otpVm.Code, _otpVm.Source);
            _inboxVm.Otp.Action = _otpVm.Action;
        }
        else
        {
            _inboxVm.Otp.HasCode = false;
        }

        _inboxVm.Items.Clear();
        foreach (var n in _recent.Take(InboxSize))
        {
            string time = n.ArrivedAt.Date == DateTime.Today
                ? n.ArrivedAt.ToString("HH:mm", CultureInfo.InvariantCulture)
                : n.ArrivedAt.ToString("M/d", CultureInfo.InvariantCulture);
            string body = n.Body.Replace('\n', ' ');
            _inboxVm.Items.Add(new InboxItem($"{SourceName(n)} · {n.Title}", body, time));
        }
        _inboxVm.Empty = _recent.Count == 0;
    }

    public ValueTask DisposeAsync()
    {
        _watcher.Dispose();
        _clipboardTimer?.Stop();
        _freshTimer?.Stop();
        return ValueTask.CompletedTask;
    }
}
