using System.Windows;
using System.Windows.Media;
using TianMu.Island.Shell;
using TianMu.Modules.Music.Views;
using TianMu.Modules.Sense.Watchers;
using TianMu.Modules.Shelf;

namespace TianMu.Tests;

public class MetaballTests
{
    [Fact]
    public void Far_apart_circles_have_no_neck() =>
        Assert.Null(Metaball.Connect(new Point(0, 0), 17, new Point(200, 0), 16));

    [Fact]
    public void Nearby_circles_get_a_neck_with_points_on_both_circles()
    {
        var c1 = new Point(0, 0);
        var c2 = new Point(40, 0);
        var neck = Metaball.Connect(c1, 17, c2, 16);

        Assert.NotNull(neck);
        var n = neck!.Value;
        Assert.Equal(17, (n.P1 - c1).Length, 3);
        Assert.Equal(17, (n.P2 - c1).Length, 3);
        Assert.Equal(16, (n.P3 - c2).Length, 3);
        Assert.Equal(16, (n.P4 - c2).Length, 3);
        Assert.True(n.P1.Y * n.P2.Y < 0, "上下兩條曲線分居中線兩側");
    }

    [Fact]
    public void Fully_overlapping_circles_have_no_neck() =>
        Assert.Null(Metaball.Connect(new Point(0, 0), 17, new Point(0.5, 0), 16));
}

public class CoverPaletteTests
{
    private static byte[] Solid(byte r, byte g, byte b, int pixels = 64)
    {
        var data = new byte[pixels * 4];
        for (int i = 0; i < pixels; i++)
        {
            data[i * 4] = b;
            data[i * 4 + 1] = g;
            data[i * 4 + 2] = r;
            data[i * 4 + 3] = 255;
        }
        return data;
    }

    [Fact]
    public void Gray_cover_falls_back_to_gold() =>
        Assert.Equal(CoverPalette.Gold, CoverPalette.Pick(Solid(128, 128, 128)));

    [Fact]
    public void Red_cover_gives_a_tamed_red()
    {
        var color = CoverPalette.Pick(Solid(220, 30, 30));
        var (h, s, l) = CoverPalette.ToHsl(color.R, color.G, color.B);

        Assert.True(h < 0.05 || h > 0.95, $"色相應該是紅色，實際 {h}");
        Assert.InRange(s, 0.2, 0.55);   // 收斂過，不刺眼
        Assert.InRange(l, 0.6, 0.72);
    }

    [Fact]
    public void Hsl_round_trip()
    {
        var original = Color.FromRgb(0x8F, 0xB3, 0xAD);
        var (h, s, l) = CoverPalette.ToHsl(original.R, original.G, original.B);
        var back = CoverPalette.FromHsl(h, s, l);
        Assert.InRange(Math.Abs(back.R - original.R), 0, 1);
        Assert.InRange(Math.Abs(back.G - original.G), 0, 1);
        Assert.InRange(Math.Abs(back.B - original.B), 0, 1);
    }
}

public class PrivacyTests
{
    [Theory]
    [InlineData(133000000000000000L, 0L, true)]
    [InlineData(133000000000000000L, 133000000001000000L, false)]
    [InlineData(0L, 0L, false)]
    public void In_use_when_started_but_not_stopped(long start, long stop, bool expected) =>
        Assert.Equal(expected, CapabilityUsage.IsInUse(start, stop));

    [Theory]
    [InlineData("C:#Program Files#Zoom#bin#Zoom.exe", "Zoom")]
    [InlineData("C:#Users#me#AppData#Local#Discord#app-1.0#Discord.exe", "Discord")]
    [InlineData("Microsoft.WindowsCamera_8wekyb3d8bbwe", "WindowsCamera")]
    public void App_names(string key, string expected) => Assert.Equal(expected, CapabilityUsage.AppName(key));
}

public class ShelfStoreTests
{
    [Fact]
    public void Adds_newest_first_without_duplicates()
    {
        var store = new ShelfStore();
        Assert.Equal(2, store.Add([@"C:\a.txt", @"C:\b.txt"]));
        Assert.Equal(1, store.Add([@"C:\A.TXT", @"C:\c.txt"]));

        Assert.Equal(new[] { @"C:\c.txt", @"C:\b.txt", @"C:\a.txt" }, store.Paths);
    }

    [Fact]
    public void Keeps_at_most_capacity()
    {
        var store = new ShelfStore();
        store.Add(Enumerable.Range(0, 30).Select(i => $@"C:\f{i}.txt"));
        Assert.Equal(ShelfStore.Capacity, store.Paths.Count);
        Assert.Equal(@"C:\f29.txt", store.Paths[0]);
    }

    [Fact]
    public void Remove_and_prune()
    {
        var store = new ShelfStore();
        store.Add([@"C:\keep.txt", @"C:\gone.txt", @"C:\drop.txt"]);

        Assert.True(store.Remove(@"C:\DROP.txt"));
        Assert.Equal(1, store.Prune(p => !p.Contains("gone")));
        Assert.Equal(new[] { @"C:\keep.txt" }, store.Paths);
    }
}
