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
    public void Slices_grow_exactly_where_the_gold_line_has_swept()
    {
        const int n = 8;
        // 金線走到四分之一圈時：前兩格已畫出，第三格還沒開始
        double quarter = RadialGeometry.SweepMs / 4;
        var (a0, a1, _) = RadialGeometry.SliceAngles(0, n);
        var first = RadialGeometry.Slice(quarter, 0, n);
        Assert.Equal(a1, first.Edge, 6);
        Assert.True(first.Grow > 0);
        var third = RadialGeometry.Slice(quarter, 2, n);
        Assert.Equal(0, third.Grow);

        // 金線正在畫第一格時，扇區的邊緣就停在金線的位置
        double early = RadialGeometry.SweepMs / 16;
        var partial = RadialGeometry.Slice(early, 0, n);
        Assert.Equal(RadialGeometry.SweepAngle(early, n), partial.Edge, 6);
        Assert.True(partial.Edge > a0 && partial.Edge < a1);

        // 全部畫完：每一格都完整、長到原尺寸、文字都在
        for (int i = 0; i < n; i++)
        {
            var end = RadialGeometry.Slice(RadialGeometry.OpenDuration.TotalMilliseconds, i, n);
            Assert.Equal(RadialGeometry.SliceAngles(i, n).End, end.Edge, 6);
            Assert.Equal(1, end.Grow, 3);
            Assert.Equal(1, end.TextOpacity);
        }
    }

    [Fact]
    public void Furnace_slides_straight_into_the_hub()
    {
        var (scaleStart, offsetStart) = RadialGeometry.FurnaceAbsorb(0);
        Assert.Equal(1, scaleStart);
        Assert.Equal(0, offsetStart, 6);
        // 結束時：扇區中心剛好落在陣眼中心（縮放後的位置 + 位移 = 0）
        var (scaleEnd, offsetEnd) = RadialGeometry.FurnaceAbsorb(1);
        double rm = (RadialGeometry.HubRadius + RadialGeometry.OuterRadius) / 2;
        Assert.Equal(0, scaleEnd * rm + offsetEnd, 6);
        Assert.Equal(0, RadialGeometry.AbsorbProgress(0));
        Assert.Equal(1, RadialGeometry.AbsorbProgress(RadialGeometry.AbsorbMs), 6);
    }
}
