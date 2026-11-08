using System.Windows;
using System.Windows.Threading;
using TianMu.Core;
using TianMu.Modules.Cleanse.Views;

namespace TianMu.Modules.Cleanse;

/// <summary>
/// 法器「洗髓」：磁碟空間守護。
///   · 低空間提醒：定時看系統碟剩多少（不掃描、不耗資源），低於門檻時天幕亮朱砂，問你要不要看看
///   · 審查：列出可清的快取、你自己的大檔案與資料夾、資源回收筒，逐項說明用途與影響
///   · 清理：只清你勾選的，動手前逐項複核；你的檔案預設先移到資源回收筒
/// 功能構想參考 RabbittuX/purrge；程式與規則為天幕島自行撰寫。
/// </summary>
public sealed class CleanseModule : IIslandModule, IDescribedModule
{
    public const string AlertOption = "alert";
    public const string ThresholdChoice = "threshold";
    public const string IntervalChoice = "interval";
    public const string OptionalChoice = "optional";

    private static readonly TimeSpan AlertCooldown = TimeSpan.FromHours(6);
    private static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(2);

    private IIslandHost? _host;
    private DispatcherTimer? _timer;
    private CleanseWindow? _window;
    private DateTime _lastAlert = DateTime.MinValue;

    public string Id => "tianmu.cleanse";
    public string DisplayName => "洗髓";
    public string Description => "磁碟快滿時提醒你，找出能清的快取與大檔案，逐項說明；你勾選確認後才清，你的檔案先進資源回收筒。";

    public IReadOnlyList<ModuleOption> Options { get; } =
    [
        new(AlertOption, "低空間提醒", "系統碟剩餘空間低於門檻時，在天幕上提醒（同一天最多每 6 小時一次）。", true),
    ];

    public IReadOnlyList<ModuleChoice> Choices { get; } =
    [
        new(ThresholdChoice, "提醒門檻", "系統碟剩下多少時提醒。",
            [new("5", "5 GB"), new("10", "10 GB"), new("15", "15 GB"), new("20", "20 GB"), new("30", "30 GB"), new("50", "50 GB")], "15"),
        new(IntervalChoice, "多久看一次", "只讀取剩餘空間，幾乎不耗資源。",
            [new("15", "15 分"), new("30", "30 分"), new("60", "1 小時"), new("180", "3 小時")], "30"),
        new(OptionalChoice, "「你決定」的項目", "你自己的檔案清理時要怎麼處理。",
            [new("recycle", "移到資源回收筒"), new("delete", "直接刪除")], "recycle"),
    ];

    public async Task InitializeAsync(IIslandHost host, CancellationToken cancellationToken)
    {
        _host = host;
        await host.InvokeAsync(() =>
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FirstCheck };
            _timer.Tick += (_, _) => Check();
            _timer.Start();
        });
        host.AddRadialAction(new RadialAction(Id, "洗髓", "清理磁碟", () => Open(scan: true), Order: 70, ToneKey: "ZhuShaBrush", DefaultVisible: false));
    }

    private IModuleSettings Settings => _host!.GetModuleSettings(Id);

    private static int ReadInt(IModuleSettings s, string key, int fallback) =>
        int.TryParse(s.GetString(key, fallback.ToString()), out int v) && v > 0 ? v : fallback;

    // ═══════════════════════ 低空間提醒 ═══════════════════════

    private void Check()
    {
        if (_host is null || _timer is null) return;
        var s = Settings;
        _timer.Interval = TimeSpan.FromMinutes(ReadInt(s, IntervalChoice, 30));
        if (!s.GetBool(AlertOption, true)) return;

        var (free, total) = DiskScanner.VolumeStatus();
        long threshold = ReadInt(s, ThresholdChoice, 15) * CleanseRules.GB;
        if (!ShouldAlert(free, total, threshold, _lastAlert, DateTime.Now, AlertCooldown)) return;
        _lastAlert = DateTime.Now;

        var vm = new LowSpaceViewModel
        {
            Title = $"{DiskScanner.SystemVolume.TrimEnd('\\')} 只剩 {CleanseJudge.Size(free)}",
            Subtitle = $"低於你設的 {CleanseJudge.Size(threshold)}；看看有什麼能清",
            FreeFraction = total > 0 ? free / (double)total : 0,
            OpenCommand = new RelayCommand(() => { _host.Dismiss(Id); Open(scan: true); }),
            LaterCommand = new RelayCommand(() => _host.Dismiss(Id)),
        };
        _host.Post(new IslandActivity(Id, vm.Title, vm.Subtitle, ActivityPriority.Alert, TimeSpan.FromSeconds(15))
        {
            ExpandedView = new LowSpaceView { DataContext = vm },
            GlowKey = "ZhuShaBrush",
        });
    }

    /// <summary>該不該提醒：低於門檻，而且距離上次提醒超過冷卻時間。</summary>
    internal static bool ShouldAlert(long free, long total, long threshold, DateTime lastAlert, DateTime now, TimeSpan cooldown) =>
        total > 0 && free < threshold && now - lastAlert >= cooldown;

    // ═══════════════════════ 審查視窗 ═══════════════════════

    private void Open(bool scan)
    {
        if (_window is { IsLoaded: true })
        {
            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
            _window.Activate();
            return;
        }

        var vm = new CleanseViewModel(
            () => Settings.GetString(OptionalChoice, "recycle") != "delete",
            (title, text) => _host?.Post(new IslandActivity(Id, title, text, ActivityPriority.Normal, TimeSpan.FromSeconds(5))
            {
                GlowKey = "TianQingBrush",
            }));
        _window = new CleanseWindow(vm);
        _window.Closed += (_, _) => _window = null;
        _window.Show();
        _window.Activate();
        if (scan) _ = vm.ScanAsync();
    }

    public ValueTask DisposeAsync()
    {
        _host?.InvokeAsync(() =>
        {
            _timer?.Stop();
            _window?.Close();
        });
        return ValueTask.CompletedTask;
    }
}
