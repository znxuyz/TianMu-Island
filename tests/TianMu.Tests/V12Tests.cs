using TianMu.Core;
using TianMu.Modules.Focus;
using TianMu.Modules.Focus.Hydration;
using TianMu.Modules.Sense.Watchers;

namespace TianMu.Tests;

public class HydrationFixedTimesTests
{
    private static readonly HydrationPlanner Planner = HydrationPlanner.Default;
    private static readonly IReadOnlyList<TimeOnly> Times = HydrationPlanner.ParseTimes("10:00, 14:30、16:00");

    [Fact]
    public void Parses_time_lists_in_any_separator()
    {
        Assert.Equal(new[] { new TimeOnly(10, 0), new TimeOnly(14, 30), new TimeOnly(16, 0) }, Times);
        Assert.Equal(new[] { new TimeOnly(9, 0), new TimeOnly(21, 5) }, HydrationPlanner.ParseTimes("21:05；9：00 亂寫"));
        Assert.Empty(HydrationPlanner.ParseTimes(""));
    }

    [Fact]
    public void Reminds_once_per_slot()
    {
        var day = new DateTime(2026, 11, 6);
        var state = new HydrationState { Day = day };

        Assert.False(Planner.IsDueAt(state, day.AddHours(9.9), Times));     // 還沒到 10:00
        Assert.True(Planner.IsDueAt(state, day.AddHours(10.1), Times));     // 10:06

        state.LastPrompted = day.AddHours(10.1);
        Assert.False(Planner.IsDueAt(state, day.AddHours(10.2), Times));    // 同一個時刻不重複
        Assert.True(Planner.IsDueAt(state, day.AddHours(14.6), Times));     // 下一個時刻
    }

    [Fact]
    public void Missed_slots_are_not_replayed_and_drinking_counts()
    {
        var day = new DateTime(2026, 11, 6);
        var state = new HydrationState { Day = day };
        Assert.False(Planner.IsDueAt(state, day.AddHours(12), Times));      // 10:00 已過兩小時

        state.LastDrink = day.AddHours(14.55);
        Assert.False(Planner.IsDueAt(state, day.AddHours(14.7), Times));    // 14:30 之後喝過了
    }

    [Fact]
    public void Snooze_brings_it_back()
    {
        var day = new DateTime(2026, 11, 6);
        var state = new HydrationState { Day = day, LastPrompted = day.AddHours(10.1) };
        Planner.Snooze(state, day.AddHours(10.1));
        Assert.False(Planner.IsDueAt(state, day.AddHours(10.2), Times));
        Assert.True(Planner.IsDueAt(state, day.AddHours(10.4), Times));
    }
}

public class DeviceNameTests
{
    [Theory]
    [InlineData("Logitech USB Receiver", true)]
    [InlineData("AirPods Pro", true)]
    [InlineData("A1B2C3", false)]
    [InlineData("12:34:56:78:9A:BC", false)]
    [InlineData("USB\\VID_046D", false)]
    [InlineData("", false)]
    [InlineData("ab", false)]
    public void Meaningful_names(string name, bool expected) => Assert.Equal(expected, DeviceNames.IsMeaningful(name));

    [Theory]
    [InlineData("Generic USB Hub", false)]
    [InlineData("USB Root Hub (USB 3.0)", false)]
    [InlineData("USB 大容量存放裝置", false)]
    [InlineData("USB Composite Device", false)]
    [InlineData("iPhone", true)]
    public void Skips_plumbing_devices(string name, bool expected) => Assert.Equal(expected, DeviceNames.IsWorthShowing(name));
}

public class FocusChoiceTests
{
    private sealed class Settings(Dictionary<string, string> values) : IModuleSettings
    {
        public bool GetBool(string key, bool defaultValue) => defaultValue;
        public string GetString(string key, string defaultValue) => values.TryGetValue(key, out var v) ? v : defaultValue;
    }

    [Fact]
    public void Durations_follow_the_user_choice()
    {
        var config = FocusModule.ConfigFrom(new Settings(new() { [FocusModule.FocusMinutesChoice] = "45", [FocusModule.BreakMinutesChoice] = "10" }));
        Assert.Equal(TimeSpan.FromMinutes(45), config.Focus);
        Assert.Equal(TimeSpan.FromMinutes(10), config.ShortBreak);
        Assert.Equal(TimeSpan.FromMinutes(30), config.LongBreak);
    }

    [Fact]
    public void Bad_values_fall_back_to_defaults()
    {
        var config = FocusModule.ConfigFrom(new Settings(new() { [FocusModule.FocusMinutesChoice] = "abc", [FocusModule.BreakMinutesChoice] = "-3" }));
        Assert.Equal(TimeSpan.FromMinutes(25), config.Focus);
        Assert.Equal(TimeSpan.FromMinutes(5), config.ShortBreak);
    }
}
