using System.Windows;
using System.Windows.Media;
using TianMu.Island.Shell;
using TianMu.Modules.Sense.Watchers;

namespace TianMu.Tests;

public class DriveDiffTests
{
    private static Dictionary<string, DriveSnapshot> Drives(params string[] names) =>
        names.ToDictionary(n => n, n => new DriveSnapshot(n, "", 0, 0), StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Detects_arrival_and_removal()
    {
        var (added, removed) = DriveDiff.Compare(Drives(@"C:\", @"E:\"), Drives(@"C:\", @"F:\"));

        Assert.Equal(@"F:\", Assert.Single(added).Name);
        Assert.Equal(@"E:\", Assert.Single(removed).Name);
    }

    [Fact]
    public void No_change_means_no_events()
    {
        var (added, removed) = DriveDiff.Compare(Drives(@"C:\"), Drives(@"C:\"));
        Assert.Empty(added);
        Assert.Empty(removed);
    }

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(850L * 1024 * 1024, "850 MB")]
    [InlineData(13207024435L, "12.3 GB")]
    public void Formats_sizes(long bytes, string expected) => Assert.Equal(expected, DriveDiff.FormatBytes(bytes));

    [Fact]
    public void Letter_drops_trailing_backslash() => Assert.Equal("E:", new DriveSnapshot(@"E:\", "", 0, 0).Letter);
}

public class AmbienceTests
{
    [Fact]
    public void Fullscreen_requires_covering_the_monitor()
    {
        var monitor = new Rect(0, 0, 1920, 1080);
        Assert.True(FullscreenRule.Covers(new Rect(0, 0, 1920, 1080), monitor));
        Assert.True(FullscreenRule.Covers(new Rect(-1, -1, 1922, 1082), monitor));   // 帶一點邊框
        Assert.False(FullscreenRule.Covers(new Rect(0, 0, 1920, 1040), monitor));    // 一般最大化（留工作列）
    }

    [Fact]
    public void Desktop_and_taskbar_are_not_fullscreen_apps()
    {
        Assert.True(FullscreenRule.IsShellWindow("Progman"));
        Assert.True(FullscreenRule.IsShellWindow("WorkerW"));
        Assert.False(FullscreenRule.IsShellWindow("Chrome_WidgetWin_1"));
    }

    [Fact]
    public void Magnet_pulls_toward_nearby_cursor_only()
    {
        var capsule = new Rect(0, 0, 200, 34);

        Assert.Equal(default, Magnet.Offset(new Point(100, 17), capsule, 70, 7));   // 已在上面
        Assert.Equal(default, Magnet.Offset(new Point(100, 200), capsule, 70, 7));  // 太遠

        var pull = Magnet.Offset(new Point(100, 54), capsule, 70, 7);              // 正下方 20px
        Assert.Equal(0, pull.X, 3);
        Assert.True(pull.Y > 0 && pull.Y <= 7);

        var closer = Magnet.Offset(new Point(100, 40), capsule, 70, 7);
        Assert.True(closer.Y > pull.Y, "越靠近吸力越強");
    }

    [Fact]
    public void Day_night_hits_anchor_colors()
    {
        Assert.Equal(Color.FromRgb(0x14, 0x16, 0x1B), DayNight.ColorsAt(TimeSpan.Zero).Top);
        Assert.Equal(Color.FromRgb(0x1C, 0x19, 0x15), DayNight.ColorsAt(TimeSpan.FromHours(6)).Top);
        Assert.Equal(Color.FromRgb(0x21, 0x1B, 0x13), DayNight.ColorsAt(TimeSpan.FromHours(12)).Top);
    }

    [Fact]
    public void Day_night_blends_between_anchors()
    {
        var nineAm = DayNight.ColorsAt(TimeSpan.FromHours(9)).Top;
        Assert.InRange(nineAm.R, 0x1C, 0x21);
        Assert.Equal(DayNight.ColorsAt(TimeSpan.Zero), DayNight.ColorsAt(TimeSpan.FromHours(24)));
    }
}
