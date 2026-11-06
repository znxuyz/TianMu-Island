using System.Windows.Data;
using TianMu.Core;
using TianMu.Modules.Sense.Views;
using TianMu.Modules.Sense.Watchers;

namespace TianMu.Modules.Sense;

/// <summary>
/// 法器「煉化進度」：工作列上的進度（瀏覽器下載、複製大檔、安裝程式…）直接顯示在天幕上。
///   · 進行中：收合時是小圓環＋程式名稱＋百分比；展開後是大圓環
///   · 完成時：天幕閃一下天青光，提示「煉成」
/// 進度條消失前若沒有接近完成（例如取消下載），就安靜地收起。
/// </summary>
public sealed class ProgressModule : IIslandModule, IDescribedModule
{
    private const double CompletedAbove = 0.9;
    private static readonly TimeSpan DoneDuration = TimeSpan.FromSeconds(3);

    private readonly TaskbarProgressWatcher _watcher = new();
    private IIslandHost? _host;

    // 以下只在 UI 執行緒存取
    private ProgressViewModel? _vm;
    private IslandActivity? _activity;
    private TaskbarProgress? _current;
    private int _missing;

    public string Id => "tianmu.progress";
    public string DisplayName => "煉化進度";
    public string Description => "下載、複製大檔、安裝程式時，工作列上的進度直接顯示在天幕上。";
    public IReadOnlyList<ModuleOption> Options => [];

    public async Task InitializeAsync(IIslandHost host, CancellationToken cancellationToken)
    {
        _host = host;
        await host.InvokeAsync(() =>
        {
            _vm = new ProgressViewModel();
            var badge = new ProgressRing { Width = 20, Height = 20, Thickness = 3, DataContext = _vm };
            badge.SetResourceReference(ProgressRing.RingBrushProperty, "TianQingBrush");
            badge.SetBinding(ProgressRing.ProgressProperty, new Binding(nameof(ProgressViewModel.Progress)));

            _activity = new IslandActivity(Id, DisplayName, Priority: ActivityPriority.Ambient)
            {
                CompactView = new ProgressCompactView { DataContext = _vm },
                ExpandedView = new ProgressPageView { DataContext = _vm },
                BadgeView = badge,
            };
        });

        _watcher.Changed += reading => _ = host.InvokeAsync(() => OnChanged(reading));
        _watcher.Start();
    }

    private void OnChanged(TaskbarProgress? reading)
    {
        if (_host is null || _vm is null || _activity is null) return;

        if (reading is not null)
        {
            bool isNew = _current is null;
            _missing = 0;
            _current = reading;
            _vm.App = reading.App;
            _vm.Progress = reading.Fraction;
            if (isNew) _host.Post(_activity);
            return;
        }

        if (_current is null) return;

        // 進度條偶爾會閃一下（例如換下一個檔案），連續兩次讀不到才算結束
        if (++_missing < 2)
        {
            _ = Task.Delay(TimeSpan.FromSeconds(1.6)).ContinueWith(_ => _host.InvokeAsync(() =>
            {
                if (_missing > 0 && _current is not null) OnChanged(null);
            }), TaskScheduler.Default);
            return;
        }

        var finished = _current;
        _current = null;
        _missing = 0;
        _host.Dismiss(Id);

        if (finished.Fraction >= CompletedAbove)
        {
            _host.Post(new IslandActivity(Id, $"煉成 · {finished.App}", "工作列上的進度已完成",
                ActivityPriority.Normal, DoneDuration) { GlowKey = "TianQingBrush" });
        }
    }

    public ValueTask DisposeAsync()
    {
        _watcher.Dispose();
        return ValueTask.CompletedTask;
    }
}
