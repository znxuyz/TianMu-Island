using System.Windows;
using TianMu.Island.Shell;
using TianMu.Modules.Ai.Claude;

namespace TianMu.Tests;

public class DockLayoutTests
{
    private static readonly Rect Screen = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData(100, 50, DockPosition.TopLeft)]
    [InlineData(960, 10, DockPosition.TopCenter)]
    [InlineData(1800, 200, DockPosition.TopRight)]
    [InlineData(100, 600, DockPosition.Left)]
    [InlineData(1500, 900, DockPosition.Right)]
    public void Choose_by_drop_area(double x, double y, DockPosition expected) =>
        Assert.Equal(expected, DockLayout.Choose(new Point(x, y), Screen));

    [Fact]
    public void Choose_works_on_secondary_monitor()
    {
        var right = new Rect(1920, 0, 2560, 1440);
        Assert.Equal(DockPosition.TopLeft, DockLayout.Choose(new Point(2000, 20), right));
    }

    [Fact]
    public void Top_docks_are_flush_with_the_edge()
    {
        Assert.True(DockLayout.IsFlushTop(DockPosition.TopCenter));
        Assert.True(DockLayout.IsFlushTop(DockPosition.TopRight));
        Assert.False(DockLayout.IsFlushTop(DockPosition.Left));
    }

    [Fact]
    public void Window_origin_per_dock()
    {
        var work = new Rect(0, 0, 1920, 1040);
        var size = new Size(640, 260);

        Assert.Equal(new Point(640, 0), DockLayout.WindowOrigin(DockPosition.TopCenter, Screen, work, size));
        Assert.Equal(new Point(0, 0), DockLayout.WindowOrigin(DockPosition.TopLeft, Screen, work, size));
        Assert.Equal(new Point(1280, 0), DockLayout.WindowOrigin(DockPosition.TopRight, Screen, work, size));
        Assert.Equal(new Point(0, 390), DockLayout.WindowOrigin(DockPosition.Left, Screen, work, size));
        Assert.Equal(new Point(1280, 390), DockLayout.WindowOrigin(DockPosition.Right, Screen, work, size));
    }
}

public class ClaudeUsageTests
{
    private static readonly DateTimeOffset Fetched = new(2026, 11, 5, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parses_both_windows()
    {
        var usage = ClaudeUsageParser.Parse("""
            {"five_hour":{"utilization":45.0,"resets_at":"2026-11-05T10:00:00+00:00"},
             "seven_day":{"utilization":27,"resets_at":"2026-11-06T00:00:00Z"},
             "seven_day_opus":null}
            """, Fetched);

        Assert.Equal(55, usage.FiveHour!.RemainingPercent);
        Assert.Equal(73, usage.SevenDay!.RemainingPercent);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero), usage.FiveHour.ResetsAt);
    }

    [Fact]
    public void Missing_reset_time_is_allowed()
    {
        var usage = ClaudeUsageParser.Parse("""{"five_hour":{"utilization":10,"resets_at":null}}""", Fetched);
        Assert.Null(usage.FiveHour!.ResetsAt);
        Assert.Null(usage.SevenDay);
    }

    [Fact]
    public void Remaining_is_clamped()
    {
        var usage = ClaudeUsageParser.Parse("""{"five_hour":{"utilization":120}}""", Fetched);
        Assert.Equal(0, usage.FiveHour!.RemainingPercent);
    }

    [Theory]
    [InlineData("""{"something":"else"}""")]
    [InlineData("""[1,2,3]""")]
    public void Unknown_shape_is_rejected(string json) =>
        Assert.Throws<FormatException>(() => ClaudeUsageParser.Parse(json, Fetched));

    [Fact]
    public void Reset_text_uses_today_tomorrow_or_date()
    {
        var now = new DateTime(2026, 11, 5, 9, 0, 0, DateTimeKind.Local);
        DateTimeOffset At(DateTime local) => new(local);

        Assert.Equal("今天 18:00 重置", ClaudeUsageParser.ResetText(At(new DateTime(2026, 11, 5, 18, 0, 0)), now));
        Assert.Equal("明天 08:00 重置", ClaudeUsageParser.ResetText(At(new DateTime(2026, 11, 6, 8, 0, 0)), now));
        Assert.Equal("11月9日 08:00 重置", ClaudeUsageParser.ResetText(At(new DateTime(2026, 11, 9, 8, 0, 0)), now));
        Assert.Equal(string.Empty, ClaudeUsageParser.ResetText(null, now));
    }

    [Theory]
    [InlineData(54.6, "55%")]
    [InlineData(72.5, "73%")]
    [InlineData(0, "0%")]
    public void Percent_text(double value, string expected) => Assert.Equal(expected, ClaudeUsageParser.PercentText(value));
}

public class ClaudeCodeCredentialsTests
{
    [Fact]
    public void Reads_token_expiry_and_plan()
    {
        var login = ClaudeCodeCredentials.Parse("""
            {"claudeAiOauth":{"accessToken":"tok-123","refreshToken":"r","expiresAt":1793860000000,"subscriptionType":"max"}}
            """);

        Assert.NotNull(login);
        Assert.Equal("tok-123", login!.AccessToken);
        Assert.Equal("max", login.Plan);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1793860000000), login.ExpiresAt);
    }

    [Fact]
    public void Expiry_check_has_a_minute_of_margin()
    {
        var login = new ClaudeCodeLogin("t", new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero), null);
        Assert.False(login.IsExpired(new DateTimeOffset(2026, 11, 5, 9, 58, 0, TimeSpan.Zero)));
        Assert.True(login.IsExpired(new DateTimeOffset(2026, 11, 5, 9, 59, 30, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData("""{"other":{}}""")]
    [InlineData("""{"claudeAiOauth":{"accessToken":""}}""")]
    [InlineData("not json")]
    public void Bad_files_return_null(string json) => Assert.Null(ClaudeCodeCredentials.Parse(json));
}
