using System.Windows.Threading;
using TianMu.Core;
using TianMu.Modules.Sense.Views;
using TianMu.Modules.Sense.Watchers;

namespace TianMu.Modules.Sense;

/// <summary>
/// 法器「周天運轉」：CPU、記憶體與網路上下行速度。
///   · 展開天幕後有一頁「周天運轉」：兩個圓環＋網速
///   · 可選：收合時在膠囊裡顯示 CPU 與記憶體（預設關閉，免得太吵）
/// 每 2 秒取樣一次，只用 Windows 內建的輕量 API。
/// </summary>
public sealed class SystemMonitorModule : IIslandModule, IDescribedModule
{
    public const string CompactOption = "compact";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly SystemSampler _sampler = new();
    private IIslandHost? _host;
    private SystemViewModel? _vm;
    private DispatcherTimer? _timer;
    private IslandActivity? _ambient;
    private bool _ambientShown;

    public string Id => "tianmu.system";
    public string DisplayName => "周天運轉";
    public string Description => "CPU、記憶體與網路速度；展開天幕後有一頁，也可以常駐在收合的膠囊上。";

    public IReadOnlyList<ModuleOption> Options { get; } =
    [
        new(CompactOption, "收合時顯示", "在收合的膠囊裡顯示 CPU 與記憶體使用率。", false),
    ];

    public async Task InitializeAsync(IIslandHost host, CancellationToken cancellationToken)
    {
        _host = host;
        await host.InvokeAsync(() =>
        {
            _vm = new SystemViewModel();
            var page = new SystemPageView { DataContext = _vm };
            _ambient = new IslandActivity(Id, DisplayName, Priority: ActivityPriority.Ambient)
            {
                CompactView = new SystemCompactView { DataContext = _vm },
                ExpandedView = page,
            };
            host.SetPage(new IslandActivity(Id, DisplayName) { ExpandedView = page });
            host.AddRadialAction(new RadialAction(Id, "周天", "系統", () => host.ShowPage(Id), Order: 60, ToneKey: "TianQingBrush"));

            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Interval };
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
            _sampler.Sample(); // 建立基準
        });
    }

    private void Tick()
    {
        if (_host is null || _vm is null || _ambient is null) return;
        if (_sampler.Sample() is { } stats) _vm.Update(stats);

        // 「收合時顯示」可以隨時在設定裡切換
        bool wantCompact = _host.GetModuleSettings(Id).GetBool(CompactOption, false);
        if (wantCompact && !_ambientShown)
        {
            _host.Post(_ambient);
            _ambientShown = true;
        }
        else if (!wantCompact && _ambientShown)
        {
            _host.Dismiss(Id);
            _ambientShown = false;
        }
    }

    public ValueTask DisposeAsync()
    {
        _timer?.Stop();
        return ValueTask.CompletedTask;
    }
}
