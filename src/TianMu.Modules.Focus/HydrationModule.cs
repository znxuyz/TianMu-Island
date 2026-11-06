using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using TianMu.Core;
using TianMu.Modules.Focus.Hydration;
using TianMu.Modules.Focus.Views;

namespace TianMu.Modules.Focus;

/// <summary>
/// 法器「清泉」：定時提醒喝水。
///   · 預設每 45 分鐘一次、每日 8 盞，只在 08:00–22:00 提醒
///   · 番茄鐘專注時不打擾，等到調息再說
///   · 人不在電腦前（超過 5 分鐘沒動滑鼠鍵盤）時不提醒，回來後才提醒
/// </summary>
public sealed class HydrationModule : IIslandModule, IDescribedModule
{
    public const string RespectFocusOption = "respectFocus";
    public const string IntervalChoice = "interval";
    public const string HoursChoice = "hours";
    public const string GoalChoice = "goal";
    public const string TimesText = "times";

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AwayThreshold = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ReminderVisibleFor = TimeSpan.FromSeconds(60);

    private HydrationPlanner _planner = HydrationPlanner.Default;
    private readonly DateTime _activeSince = DateTime.Now;
    private IIslandHost? _host;
    private HydrationState _state = new();
    private string _statePath = string.Empty;

    // 以下只在 UI 執行緒存取
    private WaterViewModel? _vm;
    private WaterReminderView? _view;
    private DispatcherTimer? _checker;
    private DispatcherTimer? _autoHide;
    private bool _showing;

    public string Id => "tianmu.hydration";
    public string DisplayName => "清泉";
    public string Description => "每 45 分鐘提醒你喝一盞水，並記錄今日飲水，目標每日 8 盞。";

    public IReadOnlyList<ModuleOption> Options { get; } =
    [
        new(RespectFocusOption, "專注時不打擾", "番茄鐘進行時，延到調息再提醒。", true),
    ];

    public IReadOnlyList<ModuleChoice> Choices { get; } =
    [
        new(IntervalChoice, "提醒間隔", "距離上次喝水多久提醒一次。",
            [new("30", "30 分"), new("45", "45 分"), new("60", "1 小時"), new("90", "1.5 小時"), new("120", "2 小時")], "45"),
        new(HoursChoice, "提醒時段", "這段時間之外不打擾。",
            [new("08-22", "08–22 點"), new("09-18", "09–18 點"), new("07-23", "07–23 點"), new("all", "全天")], "08-22"),
        new(GoalChoice, "每日目標", "一天喝幾盞；達成後今天就不再提醒。",
            [new("6", "6 盞"), new("8", "8 盞"), new("10", "10 盞")], "8"),
    ];

    public IReadOnlyList<ModuleText> Texts { get; } =
    [
        new(TimesText, "指定時刻", "填了就改成只在這些時刻提醒，不再按間隔（以逗號分隔）。", "", "例如 10:00, 14:30, 16:00"),
    ];

    /// <summary>依使用者設定組出提醒規則。</summary>
    private HydrationPlanner Planner()
    {
        var s = _host?.GetModuleSettings(Id);
        int minutes = int.TryParse(s?.GetString(IntervalChoice, "45"), out int m) && m is >= 5 and <= 600 ? m : 45;
        int goal = int.TryParse(s?.GetString(GoalChoice, "8"), out int g) && g is >= 1 and <= 30 ? g : 8;
        var (start, end) = (s?.GetString(HoursChoice, "08-22") ?? "08-22") switch
        {
            "09-18" => (new TimeOnly(9, 0), new TimeOnly(18, 0)),
            "07-23" => (new TimeOnly(7, 0), new TimeOnly(23, 0)),
            "all" => (TimeOnly.MinValue, TimeOnly.MaxValue),
            _ => (new TimeOnly(8, 0), new TimeOnly(22, 0)),
        };
        return new HydrationPlanner(TimeSpan.FromMinutes(minutes), goal, start, end);
    }

    private bool AllDay => _host?.GetModuleSettings(Id).GetString(HoursChoice, "08-22") == "all";

    public async Task InitializeAsync(IIslandHost host, CancellationToken cancellationToken)
    {
        _host = host;
        _statePath = Path.Combine(host.GetDataDirectory(Id), "hydration.json");
        _state = HydrationState.Load(_statePath);

        await host.InvokeAsync(() =>
        {
            _vm = new WaterViewModel
            {
                DrinkCommand = new RelayCommand(Drink),
                LaterCommand = new RelayCommand(Later),
            };
            _view = new WaterReminderView { DataContext = _vm };

            _checker = new DispatcherTimer(DispatcherPriority.Background) { Interval = CheckInterval };
            _checker.Tick += (_, _) => Check();
            _checker.Start();

            _autoHide = new DispatcherTimer(DispatcherPriority.Normal) { Interval = ReminderVisibleFor };
            _autoHide.Tick += (_, _) =>
            {
                // 沒有回應就當作「稍後」
                _autoHide.Stop();
                if (_showing) Later();
            };

            // 專注結束（進入調息）時，馬上看看是否該喝水了
            host.FocusModeChanged += focusing => { if (!focusing) Check(); };
        });

        host.AddQuickAction(new QuickAction(Id, "飲水", Drink, Order: 10));
    }

    private void Check()
    {
        if (_host is null || _showing) return;

        var settings = _host.GetModuleSettings(Id);
        if (settings.GetBool(RespectFocusOption, true) && _host.IsFocusMode) return;
        if (UserIdleTime() > AwayThreshold) return;

        var now = DateTime.Now;
        _planner = Planner();
        var times = HydrationPlanner.ParseTimes(settings.GetString(TimesText, string.Empty));
        bool due = times.Count > 0
            ? _planner.IsDueAt(_state, now, times)
            : _planner.IsDue(_state, now, _activeSince, limitToDaytime: !AllDay);
        if (!due) return;

        ShowReminder(now);
    }

    private void ShowReminder(DateTime now)
    {
        if (_host is null || _vm is null || _view is null) return;

        _vm.Detail = _state.LastDrink is { } last && last.Date == now.Date
            ? $"上回飲水已是 {(int)(now - last).TotalMinutes} 分鐘前 · 今日 {_state.Cups} / {_planner.DailyGoal} 盞"
            : $"今日第一盞 · 目標 {_planner.DailyGoal} 盞";
        _vm.Cups = Enumerable.Range(0, _planner.DailyGoal).Select(i => i < _state.Cups).ToList();

        _host.Post(new IslandActivity(Id, "該飲一盞清泉了", null, ActivityPriority.Normal, null)
        {
            ExpandedView = _view,
            GlowKey = "TianQingBrush",
        });
        _showing = true;
        _state.LastPrompted = now;
        _state.SnoozedUntil = null; // 這次提醒已經跳出；之後按「稍後」會再設定
        Save();
        _autoHide?.Stop();
        _autoHide?.Start();
    }

    private void Drink()
    {
        if (_host is null) return;
        var now = DateTime.Now;
        _planner.RecordDrink(_state, now);
        Save();

        HideReminder();
        string goal = _state.Cups >= _planner.DailyGoal ? " · 今日已圓滿" : string.Empty;
        _host.Post(new IslandActivity(Id, "已飲一盞", $"今日 {_state.Cups} / {_planner.DailyGoal} 盞{goal}",
            ActivityPriority.Normal, TimeSpan.FromSeconds(2.5)));
    }

    private void Later()
    {
        _planner.Snooze(_state, DateTime.Now);
        Save();
        HideReminder();
    }

    private void HideReminder()
    {
        _autoHide?.Stop();
        if (!_showing) return;
        _showing = false;
        _host?.Dismiss(Id);
    }

    private void Save()
    {
        try { _state.Save(_statePath); }
        catch (Exception ex) { Debug.WriteLine($"[清泉] 存檔失敗：{ex.Message}"); }
    }

    // ── 使用者是否在電腦前 ──────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    private static TimeSpan UserIdleTime()
    {
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        uint idleMs = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(idleMs);
    }

    public ValueTask DisposeAsync()
    {
        _checker?.Stop();
        _autoHide?.Stop();
        Save();
        return ValueTask.CompletedTask;
    }
}
