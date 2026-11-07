using System.Windows;
using TianMu.Island.Shell;

namespace TianMu.Tests;

public class RadialGeometryTests
{
    private static readonly Point C = new(200, 200);

    [Fact]
    public void First_slice_is_at_the_top_and_order_runs_clockwise()
    {
        double r = 100;
        Assert.Equal(0, RadialGeometry.HitTest(new Point(C.X, C.Y - r), C, 8));      // 正上方
        Assert.Equal(2, RadialGeometry.HitTest(new Point(C.X + r, C.Y), C, 8));      // 右方
        Assert.Equal(4, RadialGeometry.HitTest(new Point(C.X, C.Y + r), C, 8));      // 正下方
        Assert.Equal(6, RadialGeometry.HitTest(new Point(C.X - r, C.Y), C, 8));      // 左方
        Assert.Equal(7, RadialGeometry.HitTest(new Point(C.X - 50, C.Y - 90), C, 8)); // 左上
    }

    [Fact]
    public void Hub_cancels_and_far_outside_is_off_the_dial()
    {
        Assert.Equal(-1, RadialGeometry.HitTest(new Point(C.X + 10, C.Y + 10), C, 8));
        Assert.Equal(-2, RadialGeometry.HitTest(new Point(C.X + 400, C.Y), C, 8));
        // 剛超出外緣一點點仍算扇區（拖曳時手會滑出去）
        Assert.Equal(2, RadialGeometry.HitTest(new Point(C.X + RadialGeometry.OuterRadius + 20, C.Y), C, 8));
        Assert.Equal(-2, RadialGeometry.HitTest(new Point(C.X + RadialGeometry.OuterRadius + 20, C.Y), C, 8, slack: 0));
    }

    [Fact]
    public void Slices_leave_a_gap_and_cover_the_circle()
    {
        double total = 0;
        for (int i = 0; i < 8; i++)
        {
            var (a0, a1, mid) = RadialGeometry.SliceAngles(i, 8);
            Assert.True(a1 > a0);
            Assert.Equal((a0 + a1) / 2, mid, 9);
            total += a1 - a0 + 2 * RadialGeometry.Gap;
        }
        Assert.Equal(2 * Math.PI, total, 6);
    }

    [Fact]
    public void Opening_draws_the_gold_line_first_then_slices_follow_clockwise()
    {
        Assert.Equal(0, RadialGeometry.Slice(200, 0).Opacity);          // 金線還在畫，扇區未出
        Assert.True(RadialGeometry.OutlineProgress(200) > 0.5);
        Assert.True(RadialGeometry.Slice(500, 0).Opacity > RadialGeometry.Slice(500, 3).Opacity); // 順時針接力
        var end = RadialGeometry.Slice(RadialGeometry.OpenDuration.TotalMilliseconds, 7);
        Assert.Equal(1, end.Scale, 3);
        Assert.Equal(1, end.Opacity);
        Assert.Equal(1, end.TextOpacity);
    }
}
