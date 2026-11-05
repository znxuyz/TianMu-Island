using TianMu.Core;
using TianMu.Modules.Focus.Cultivation;
using TianMu.Modules.Focus.Hydration;
using TianMu.Modules.Focus.Pomodoro;
using TianMu.Modules.Focus.Views;

namespace TianMu.Tests;

public class RealmTests
{
    [Theory]
    [InlineData(0, IslandRealm.LianQi, 0)]
    [InlineData(599, IslandRealm.LianQi, 2)]
    [InlineData(600, IslandRealm.ZhuJi, 0)]
    [InlineData(1500, IslandRealm.ZhuJi, 1)]       // 25 時：築基（10~50 時）的中段
    [InlineData(3000, IslandRealm.JinDan, 0)]
    [InlineData(9000, IslandRealm.YuanYing, 0)]
    [InlineData(30000, IslandRealm.HuaShen, 0)]
    [InlineData(999999, IslandRealm.HuaShen, 2)]
    public void Thresholds(long minutes, IslandRealm realm, int stage)
    {
        var s = Realms.Evaluate(minutes);
        Assert.Equal(realm, s.Realm);
        Assert.Equal(stage, s.Stage);
    }

    [Fact]
    public void Minutes_to_next_realm()
    {
        Assert.Equal(600, Realms.Evaluate(0).MinutesToNextRealm);
        Assert.Equal(160, Realms.Evaluate(2840).MinutesToNextRealm);
        Assert.Null(Realms.Evaluate(30000).MinutesToNextRealm);
    }

    [Fact]
    public void Title_combines_realm_and_stage() => Assert.Equal("築基 · 中期", Realms.Evaluate(1500).Title);

    [Theory]
    [InlineData(35, "35 分")]
    [InlineData(60, "1 時")]
    [InlineData(160, "2 時 40 分")]
    public void Duration_format(long minutes, string expected) => Assert.Equal(expected, Realms.FormatDuration(minutes));
}

public class CultivationLedgerTests
{
    private static readonly DateTime Day = new(2026, 11, 5, 10, 0, 0);

    [Fact]
    public void Credit_accumulates_and_detects_breakthrough()
    {
        var ledger = new CultivationLedger { TotalMinutes = 590 };

        var result = ledger.Credit(25, Day, completedSession: true);

        Assert.True(result.BrokeThrough);
        Assert.Equal(IslandRealm.LianQi, result.Before.Realm);
        Assert.Equal(IslandRealm.ZhuJi, result.After.Realm);
        Assert.Equal(615, ledger.TotalMinutes);
        Assert.Equal(1, ledger.CompletedSessions);
        Assert.Equal(25, ledger.MinutesOn(Day));
    }

    [Fact]
    public void Zero_minutes_changes_nothing()
    {
        var ledger = new CultivationLedger();
        var result = ledger.Credit(0, Day, completedSession: false);

        Assert.False(result.BrokeThrough);
        Assert.Equal(0, ledger.TotalMinutes);
        Assert.Empty(ledger.Daily);
    }

    [Fact]
    public void Streak_counts_consecutive_days()
    {
        var ledger = new CultivationLedger();
        ledger.Credit(25, Day.AddDays(-2), false);
        ledger.Credit(25, Day.AddDays(-1), false);
        ledger.Credit(25, Day, false);

        Assert.Equal(3, ledger.StreakDays(Day));
    }

    [Fact]
    public void Streak_survives_until_today_is_practised()
    {
        var ledger = new CultivationLedger();
        ledger.Credit(25, Day.AddDays(-1), false);

        Assert.Equal(1, ledger.StreakDays(Day));        // 今天還沒修煉，不算斷
        Assert.Equal(0, ledger.StreakDays(Day.AddDays(1)));
    }

    [Fact]
    public void Save_and_load_round_trip()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ledger-{Guid.NewGuid():N}.json");
        try
        {
            var ledger = new CultivationLedger();
            ledger.Credit(125, Day, true);
            ledger.Save(path);

            var loaded = CultivationLedger.Load(path);
            Assert.Equal(125, loaded.TotalMinutes);
            Assert.Equal(125, loaded.MinutesOn(Day));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class PomodoroTimerTests
{
    private static readonly DateTime T0 = new(2026, 11, 5, 9, 0, 0);
    private static PomodoroTimer NewTimer() => new(PomodoroConfig.Default);

    [Fact]
    public void Focus_completes_into_short_break()
    {
        var timer = NewTimer();
        timer.StartFocus(T0);

        Assert.Null(timer.Tick(T0.AddMinutes(24)));
        var ev = timer.Tick(T0.AddMinutes(25));

        Assert.Equal(PomodoroEventKind.FocusCompleted, ev?.Kind);
        Assert.Equal(TimeSpan.FromMinutes(25), ev?.FocusedTime);
        Assert.Equal(PomodoroPhase.ShortBreak, timer.Phase);
        Assert.Equal(1, timer.CompletedInCycle);
    }

    [Fact]
    public void Fourth_round_earns_long_break_and_cycle_resets()
    {
        var timer = NewTimer();
        var now = T0;
        for (int i = 0; i < 4; i++)
        {
            timer.StartFocus(now);
            now = now.AddMinutes(25);
            timer.Tick(now);
            if (i < 3)
            {
                Assert.Equal(PomodoroPhase.ShortBreak, timer.Phase);
                now = now.AddMinutes(5);
                Assert.Equal(PomodoroEventKind.BreakCompleted, timer.Tick(now)?.Kind);
            }
        }

        Assert.Equal(PomodoroPhase.LongBreak, timer.Phase);
        now = now.AddMinutes(15);
        timer.Tick(now);
        Assert.Equal(PomodoroPhase.Idle, timer.Phase);
        Assert.Equal(0, timer.CompletedInCycle);
    }

    [Fact]
    public void Pause_freezes_remaining_time()
    {
        var timer = NewTimer();
        timer.StartFocus(T0);
        timer.Pause(T0.AddMinutes(10));

        Assert.Equal(TimeSpan.FromMinutes(15), timer.Remaining(T0.AddMinutes(40)));
        Assert.Null(timer.Tick(T0.AddMinutes(40)));

        timer.Resume(T0.AddMinutes(40));
        Assert.Equal(TimeSpan.FromMinutes(15), timer.Remaining(T0.AddMinutes(40)));
        Assert.Equal(PomodoroEventKind.FocusCompleted, timer.Tick(T0.AddMinutes(55))?.Kind);
    }

    [Fact]
    public void Abandon_returns_focused_time_and_stops()
    {
        var timer = NewTimer();
        timer.StartFocus(T0);

        var focused = timer.Abandon(T0.AddMinutes(12));

        Assert.Equal(TimeSpan.FromMinutes(12), focused);
        Assert.Equal(PomodoroPhase.Idle, timer.Phase);
    }

    [Fact]
    public void Progress_and_round()
    {
        var timer = NewTimer();
        timer.StartFocus(T0);

        Assert.Equal(1, timer.CurrentRound);
        Assert.Equal(0.4, timer.Progress(T0.AddMinutes(10)), 3);
    }
}

public class HydrationPlannerTests
{
    private static readonly HydrationPlanner Planner = HydrationPlanner.Default;
    private static readonly DateTime Morning = new(2026, 11, 5, 9, 0, 0);

    [Fact]
    public void First_reminder_comes_one_interval_after_start()
    {
        var state = new HydrationState();
        Assert.False(Planner.IsDue(state, Morning.AddMinutes(44), Morning, limitToDaytime: true));
        Assert.True(Planner.IsDue(state, Morning.AddMinutes(45), Morning, limitToDaytime: true));
    }

    [Fact]
    public void Drinking_resets_the_clock()
    {
        var state = new HydrationState();
        Planner.RecordDrink(state, Morning.AddMinutes(30));

        Assert.False(Planner.IsDue(state, Morning.AddMinutes(60), Morning, true));
        Assert.True(Planner.IsDue(state, Morning.AddMinutes(75), Morning, true));
        Assert.Equal(1, state.Cups);
    }

    [Fact]
    public void Snooze_delays_by_fifteen_minutes()
    {
        var state = new HydrationState();
        var due = Morning.AddMinutes(45);
        Planner.Snooze(state, due);

        Assert.False(Planner.IsDue(state, due.AddMinutes(14), Morning, true));
        Assert.True(Planner.IsDue(state, due.AddMinutes(15), Morning, true));
    }

    [Fact]
    public void Quiet_at_night_unless_disabled()
    {
        var state = new HydrationState();
        var night = new DateTime(2026, 11, 5, 23, 0, 0);
        var start = night.AddHours(-2);

        Assert.False(Planner.IsDue(state, night, start, limitToDaytime: true));
        Assert.True(Planner.IsDue(state, night, start, limitToDaytime: false));
    }

    [Fact]
    public void Stops_after_daily_goal_and_resets_next_day()
    {
        var state = new HydrationState();
        for (int i = 0; i < 8; i++) Planner.RecordDrink(state, Morning.AddMinutes(i));

        Assert.False(Planner.IsDue(state, Morning.AddHours(5), Morning, true));

        var tomorrow = Morning.AddDays(1);
        Assert.True(Planner.IsDue(state, tomorrow.AddMinutes(45), tomorrow, true));
        Assert.Equal(0, state.Cups);
    }
}

public class ChineseNumberTests
{
    [Theory]
    [InlineData(1, "一")]
    [InlineData(10, "十")]
    [InlineData(12, "十二")]
    [InlineData(20, "二十")]
    [InlineData(34, "三十四")]
    public void Converts(int n, string expected) => Assert.Equal(expected, ChineseNumber.Of(n));
}
