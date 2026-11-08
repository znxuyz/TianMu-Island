using System.Windows;
using TianMu.Island.Shell;
using C = TianMu.Island.Shell.RadialLayout.Candidate;

namespace TianMu.Tests;

public class RadialLayoutTests
{
    // 預設的十格＋五個預設不放的候選
    private static readonly C[] Catalog =
    [
        new("凝神", 10, true), new("傳音", 15, false), new("清泉", 20, true), new("琴音", 24, false), new("換曲", 26, false),
        new("記一筆", 30, true), new("天機簿", 32, false), new("萬象簿", 35, true), new("乾坤袋", 40, true), new("照妖鏡", 45, true),
        new("化形爐", 50, true), new("周天", 60, true), new("靈石", 65, false), new("隱藏", 90, true), new("設定", 100, true),
    ];

    [Fact]
    public void Default_is_the_ten_default_slices_in_order()
    {
        var keys = RadialLayout.Arrange(Catalog, null);
        Assert.Equal(["凝神", "清泉", "記一筆", "萬象簿", "乾坤袋", "照妖鏡", "化形爐", "周天", "隱藏", "設定"], keys);
    }

    [Fact]
    public void Custom_order_is_kept_and_unknown_or_duplicate_keys_are_dropped()
    {
        var keys = RadialLayout.Arrange(Catalog, ["琴音", "設定", "不存在", "凝神", "琴音", "靈石"]);
        Assert.Equal(["琴音", "設定", "凝神", "靈石"], keys);
    }

    [Fact]
    public void Never_more_than_ten()
    {
        var keys = RadialLayout.Arrange(Catalog, Catalog.Select(c => c.Key).ToList());
        Assert.Equal(RadialLayout.Max, keys.Count);
    }

    [Fact]
    public void Topped_up_to_four_when_chosen_slices_disappear()
    {
        // 例如自訂了兩格，其中一個法器之後被關掉
        var keys = RadialLayout.Arrange(Catalog.Where(c => c.Key != "琴音"), ["琴音", "靈石"]);
        Assert.Equal(["靈石", "凝神", "清泉", "記一筆"], keys);
    }

    [Fact]
    public void Toggle_respects_the_limits()
    {
        var four = new List<string> { "a", "b", "c", "d" };
        Assert.False(RadialLayout.TryToggle(four, "a", on: false));
        Assert.Equal(4, four.Count);
        Assert.True(RadialLayout.TryToggle(four, "e", on: true));
        Assert.True(RadialLayout.TryToggle(four, "a", on: false));
        Assert.Equal(["b", "c", "d", "e"], four);

        var ten = Enumerable.Range(0, 10).Select(i => i.ToString()).ToList();
        Assert.False(RadialLayout.TryToggle(ten, "x", on: true));
        Assert.Equal(10, ten.Count);
    }

    // ── 展開位置：避開天幕、留在螢幕內 ──

    private static readonly Rect Work = new(0, 0, 1920, 1040);

    [Fact]
    public void Dial_opened_on_a_top_island_moves_below_it()
    {
        var island = new Rect(750, 0, 420, 108);          // 頂端正中、展開成中形
        var c = RadialGeometry.PlaceCenter(new Point(960, 40), 200, island, Work);
        Assert.False(RadialGeometry.Overlaps(c, 200, island));
        Assert.Equal(960, c.X, 6);                         // 只往下移，不左右亂跑
        Assert.Equal(108 + 10 + 200, c.Y, 6);
    }

    [Fact]
    public void Dial_beside_a_right_docked_island_moves_left()
    {
        var island = new Rect(1886, 400, 34, 240);         // 右側直式
        var c = RadialGeometry.PlaceCenter(new Point(1900, 520), 200, island, Work);
        Assert.False(RadialGeometry.Overlaps(c, 200, island));
        Assert.True(c.X + 200 <= island.Left);
        Assert.Equal(520, c.Y, 6);
    }

    [Fact]
    public void Dial_far_from_the_island_stays_at_the_cursor_and_inside_the_screen()
    {
        var island = new Rect(840, 0, 240, 34);
        Assert.Equal(new Point(500, 600), RadialGeometry.PlaceCenter(new Point(500, 600), 200, island, Work));
        var edge = RadialGeometry.PlaceCenter(new Point(10, 1030), 200, island, Work);
        Assert.Equal(new Point(200, 840), edge);
    }

    [Fact]
    public void Dial_size_defaults_to_large()
    {
        Assert.Equal(1.0, TianMu.Island.Settings.RadialSizes.Scale("standard"));
        Assert.Equal(1.25, TianMu.Island.Settings.RadialSizes.Scale(null));
        Assert.Equal(1.25, TianMu.Island.Settings.RadialSizes.Scale("large"));
        Assert.Equal(1.5, TianMu.Island.Settings.RadialSizes.Scale("huge"));
        Assert.Equal("large", new TianMu.Island.Settings.AppSettings().RadialSize);
    }

    // ── 金線輪轉：4 到 10 格都要正常 ──

    [Theory]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)]
    public void Weave_reaches_every_slice_in_clockwise_order_and_finishes(int n)
    {
        double last = -1;
        for (int i = 0; i < n; i++)
        {
            // 每一格被金線碰到的時刻：依序、且都在一圈之內
            double reached = Enumerable.Range(0, 2000).Select(ms => (double)ms)
                .First(ms => RadialGeometry.Slice(ms, i, n).Edge > RadialGeometry.SliceAngles(i, n).Start);
            Assert.True(reached > last, $"第 {i} 格應在第 {i - 1} 格之後");
            Assert.True(reached < RadialGeometry.SweepMs);
            last = reached;
        }

        // 動畫結束時：每格都畫滿、長滿、字全亮
        double end = RadialGeometry.OpenDuration.TotalMilliseconds;
        for (int i = 0; i < n; i++)
        {
            var (a0, a1, _) = RadialGeometry.SliceAngles(i, n);
            var (edge, grow, text) = RadialGeometry.Slice(end, i, n);
            Assert.Equal(a1, edge, 9);
            Assert.Equal(1, grow, 3);
            Assert.Equal(1, text, 9);
            Assert.True(a1 > a0);
        }
    }

    [Theory]
    [InlineData(4)] [InlineData(7)] [InlineData(10)]
    public void Each_slice_center_hits_itself(int n)
    {
        var c = new Point(300, 300);
        double r = (RadialGeometry.HubRadius + RadialGeometry.OuterRadius) / 2;
        for (int i = 0; i < n; i++)
        {
            double mid = RadialGeometry.SliceAngles(i, n).Mid;
            Assert.Equal(i, RadialGeometry.HitTest(new Point(c.X + r * Math.Cos(mid), c.Y + r * Math.Sin(mid)), c, n));
        }
    }

    [Theory]
    [InlineData(4)] [InlineData(10)]
    public void Gold_line_starts_on_the_first_slice_left_edge(int n)
    {
        var (a0, _, _) = RadialGeometry.SliceAngles(0, n);
        Assert.Equal(a0 - RadialGeometry.Gap, RadialGeometry.StartAngle(n), 9);
    }
}
