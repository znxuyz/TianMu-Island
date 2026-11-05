using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using TianMu.Core;
using TianMu.Modules.Focus.Cultivation;
using TianMu.Modules.Focus.Pomodoro;
using TianMu.Modules.Focus.Views;

namespace TianMu.Modules.Focus;

/// <summary>
/// 法器「專注陣法」：番茄鐘與修為合一。
///   · 每專注一回，修為漸長；累積到門檻即突破境界、播放渡劫，天幕外觀隨之進化
///   · 中途散功不扣修為：已專注的時間依然計入
///   · 專注時開啟宿主的「專注模式」，其他法器（例如飲水）會等到休息再打擾
/// </summary>
public sealed class FocusModule : IIslandModule, IDescribedModule
{
    public const string AutoBreakOption = "autoBreak";
    public const string TribulationOption = "tribulation";

    private const double CompactWidth = 232;
    private const double ExpandedWidth = 420;
    private const double ExpandedHeight = 172;
    private static readonly TimeSpan FlashDuration = TimeSpan.FromSeconds(6);

    private readonly PomodoroTimer _timer = new(PomodoroConfig.Default);
    private IIslandHost? _host;
    private CultivationLedger _ledger = new();
    private string _ledgerPath = string.Empty;

    // 以下只在 UI 執行緒存取
    private FocusViewModel? _vm;
    private FocusCompactView? _compact;
    private FocusExpandedView? _expanded;
    private ProgressRing? _badge;
    private BreakthroughViewModel? _breakVm;
    private BreakthroughView? _breakView;
    private DispatcherTimer? _ticker;
    private string? _flash;
    private DateTime _flashUntil;
    private bool _posted;

    public string Id => "tianmu.focus";
    public string DisplayName => "專注陣法";
    public string Description => "番茄鐘與修為合一：每專注一回，修為漸長，累積到門檻便渡劫突破。";

    public IReadOnlyList<ModuleOption> Options { get; } =
    [
        new(AutoBreakOption, "自動開始調息", "一回專注結束後，直接進入休息倒數。", true),
        new(TribulationOption, "渡劫特效", "突破境界時，天幕閃過雷光、金線繞行一圈。", true),
    ];

    public async Task InitializeAsync(IIslandHost host, CancellationToken cancellationToken)
    {
        _host = host;
        _ledgerPath = Path.Combine(host.GetDataDirectory(Id), "cultivation.json");
        _ledger = CultivationLedger.Load(_ledgerPath);
        host.SetRealm(_ledger.Standing.Realm);

        await host.InvokeAsync(() =>
        {
            _vm = new FocusViewModel
            {
                PauseResumeCommand = new RelayCommand(TogglePause),
                AbandonCommand = new RelayCommand(Abandon),
                SkipBreakCommand = new RelayCommand(SkipBreak),
            };
            _compact = new FocusCompactView { DataContext = _vm };
            _expanded = new FocusExpandedView { DataContext = _vm };

            // 分身小圓：只放一個小圓環
            _badge = new ProgressRing { Width = 20, Height = 20, Thickness = 3, DataContext = _vm };
            _badge.SetBinding(ProgressRing.ProgressProperty, new System.Windows.Data.Binding(nameof(FocusViewModel.Progress)));
            _breakVm = new BreakthroughViewModel();
            _breakView = new BreakthroughView { DataContext = _breakVm };

            _ticker = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(250) };
            _ticker.Tick += (_, _) => OnTick();

            RefreshRealm();
        });

        host.AddQuickAction(new QuickAction(Id, "凝神", StartFocus, Order: 0));
    }

    // ═══════════════════════ 操作 ═══════════════════════

    private void StartFocus()
    {
        if (_host is null || _vm is null) return;
        if (_timer.Phase == PomodoroPhase.Focus) return;

        _timer.StartFocus(DateTime.Now);
        _host.SetFocusMode(true);
        _flash = null;
        Show();
        Refresh();
        _ticker?.Start();
    }

    private void TogglePause()
    {
        var now = DateTime.Now;
        if (_timer.IsPaused) _timer.Resume(now);
        else _timer.Pause(now);
        Refresh();
    }

    /// <summary>散功：放棄這一回，但已專注的時間照樣計入修為。</summary>
    private void Abandon()
    {
        if (_timer.Phase != PomodoroPhase.Focus) return;

        var focused = _timer.Abandon(DateTime.Now);
        _host?.SetFocusMode(false);
        int minutes = (int)focused.TotalMinutes;
        var result = Credit(minutes, completed: false);

        Hide();
        _ticker?.Stop();

        _host?.Post(new IslandActivity(Id, "散功",
            minutes > 0 ? $"已積修為 {Realms.FormatDuration(minutes)}，下回再續" : "未及一分，下回再續",
            ActivityPriority.Normal, TimeSpan.FromSeconds(3)));

        if (result.BrokeThrough) Breakthrough(result);
    }

    private void SkipBreak()
    {
        _timer.SkipBreak();
        Hide();
        _ticker?.Stop();
    }

    // ═══════════════════════ 計時 ═══════════════════════

    private void OnTick()
    {
        var ev = _timer.Tick(DateTime.Now);
        if (ev is { Kind: PomodoroEventKind.FocusCompleted } done) OnFocusCompleted(done);
        else if (ev is { Kind: PomodoroEventKind.BreakCompleted }) OnBreakCompleted();

        if (!_timer.IsRunning) _ticker?.Stop();
        Refresh();
    }

    private void OnFocusCompleted(PomodoroEvent ev)
    {
        _host?.SetFocusMode(false);
        int minutes = (int)Math.Round(ev.FocusedTime.TotalMinutes);
        var result = Credit(minutes, completed: true);

        bool autoBreak = _host?.GetModuleSettings(Id).GetBool(AutoBreakOption, true) != false;
        if (!autoBreak) _timer.SkipBreak();

        _flash = $"一回功成 · 修為 +{Realms.FormatDuration(minutes)}";
        _flashUntil = DateTime.Now + FlashDuration;
        Refresh();

        if (_timer.IsRunning)
        {
            // 彈開展示這一回的成果（此時已進入調息）
            _host?.Post(CreateActivity(ActivityPriority.Normal, FlashDuration));
        }
        else
        {
            Hide();
            _host?.Post(new IslandActivity(Id, "一回功成", $"修為 +{Realms.FormatDuration(minutes)}",
                ActivityPriority.Normal, TimeSpan.FromSeconds(4)));
        }

        if (result.BrokeThrough) Breakthrough(result);
    }

    private void OnBreakCompleted()
    {
        Hide();
        _host?.Post(new IslandActivity(Id, "調息已畢", "展開天幕，按「凝神」再入一回",
            ActivityPriority.Normal, TimeSpan.FromSeconds(5)));
    }

    // ═══════════════════════ 修為 ═══════════════════════

    private CreditResult Credit(int minutes, bool completed)
    {
        var result = _ledger.Credit(minutes, DateTime.Now, completed);
        if (result.MinutesAdded > 0)
        {
            try { _ledger.Save(_ledgerPath); }
            catch (Exception ex) { Debug.WriteLine($"[專注陣法] 修為存檔失敗：{ex.Message}"); }
        }
        RefreshRealm();
        return result;
    }

    private void Breakthrough(CreditResult result)
    {
        if (_host is null || _breakVm is null || _breakView is null) return;

        _host.SetRealm(result.After.Realm);
        _breakVm.RealmTitle = result.After.Title;
        _breakVm.Detail = $"累積修為 {Realms.FormatDuration(_ledger.TotalMinutes)} · 道心更堅";

        _host.Post(new IslandActivity(Id, $"突破 · {result.After.RealmName}", null, ActivityPriority.Alert, TimeSpan.FromSeconds(7))
        {
            ExpandedView = _breakView,
            ExpandedWidth = 420,
            ExpandedHeight = 136,
        });

        if (_host.GetModuleSettings(Id).GetBool(TribulationOption, true))
            _host.PlayTribulation();
        _breakView.PlayStamp();
    }

    private void RefreshRealm()
    {
        if (_vm is null) return;
        var now = DateTime.Now;
        var standing = _ledger.Standing;

        _vm.RealmTitle = standing.Title;
        _vm.RealmProgress = standing.ProgressInRealm;
        _vm.RealmHint = standing.MinutesToNextRealm is { } left
            ? $"距{Realms.Name(standing.Realm + 1)}尚需 {Realms.FormatDuration(left)}"
            : "已臻化神，修為無盡";

        int today = _ledger.MinutesOn(now);
        int streak = _ledger.StreakDays(now);
        _vm.TodayText = streak > 1 ? $"今日 {Realms.FormatDuration(today)} · 連修 {streak} 日" : $"今日 {Realms.FormatDuration(today)}";

        _host?.SetStatus(Id, $"{standing.Title} · {_vm.TodayText}");
    }

    // ═══════════════════════ 畫面 ═══════════════════════

    private void Refresh()
    {
        if (_vm is null) return;
        var now = DateTime.Now;

        var remaining = _timer.Remaining(now);
        _vm.TimeText = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        _vm.Progress = _timer.Progress(now);
        _vm.IsBreak = _timer.IsBreak;
        _vm.IsPaused = _timer.IsPaused;
        _vm.RoundText = _timer.IsBreak
            ? _timer.Phase == PomodoroPhase.LongBreak ? "長休" : "小憩"
            : $"第{ChineseNumber.Of(Math.Max(1, _timer.CurrentRound))}回";

        _vm.PhaseLabel = _timer.IsPaused ? "暫歇" : _timer.IsBreak ? "調息" : "凝神";

        if (_flash is not null && now < _flashUntil) _vm.StatusLabel = _flash;
        else
        {
            _flash = null;
            _vm.StatusLabel = _timer.IsPaused ? "暫歇中 · 隨時可續"
                : _timer.IsBreak ? "調息 · 起身走走，望向遠方"
                : "凝神 · 專注中";
        }
    }

    private IslandActivity CreateActivity(ActivityPriority priority, TimeSpan? duration) =>
        new(Id, DisplayName, null, priority, duration)
        {
            CompactView = _compact,
            ExpandedView = _expanded,
            BadgeView = _badge,
            CompactWidth = CompactWidth,
            ExpandedWidth = ExpandedWidth,
            ExpandedHeight = ExpandedHeight,
        };

    private void Show()
    {
        if (_posted || _host is null) return;
        _host.Post(CreateActivity(ActivityPriority.Ambient, null));
        _posted = true;
    }

    private void Hide()
    {
        if (!_posted || _host is null) return;
        _host.Dismiss(Id);
        _posted = false;
    }

    public ValueTask DisposeAsync()
    {
        // 關閉程式時若正在專注，已專注的時間照樣記下（不可再呼叫 host.InvokeAsync）
        if (_timer.Phase == PomodoroPhase.Focus)
        {
            int minutes = (int)_timer.Abandon(DateTime.Now).TotalMinutes;
            _ledger.Credit(minutes, DateTime.Now, completedSession: false);
            try { _ledger.Save(_ledgerPath); } catch { /* 收功時忽略 */ }
        }
        _ticker?.Stop();
        return ValueTask.CompletedTask;
    }
}
