using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using TianMu.Island.Shell;

namespace TianMu.Tests;

public class IslandShapeTests
{
    private const double W = 240, H = 34, R = 17, Flare = 40;

    [Fact]
    public void Top_dock_shoulders_flare_outside_the_capsule()
    {
        var shape = IslandShape.Build(W, H, R, Flare, DockEdges.Top);
        Assert.Equal(-Flare, shape.Bounds.Left, 3);
        Assert.Equal(W + Flare, shape.Bounds.Right, 3);
        Assert.Equal(0, shape.Bounds.Top, 3);

        // 肩膀貼著上緣往外延伸：上緣外側有填色，下方外側沒有
        Assert.True(shape.FillContains(new Point(-Flare / 2, 0.5)));
        Assert.True(shape.FillContains(new Point(W + Flare / 2, 0.5)));
        Assert.False(shape.FillContains(new Point(-Flare / 2, H - 1)));
    }

    [Fact]
    public void Shoulders_are_not_clipped_by_layout()
    {
        // v0.8.0 的錯誤：形體直接放在 Grid 裡，超出膠囊的曲面被版面裁掉，看起來仍是直角
        RunOnSta(() =>
        {
            var geometry = IslandShape.Build(W, H, R, Flare, DockEdges.Top);

            var direct = new Path { Data = geometry };
            var clipped = new Grid { Width = W, Height = H, Children = { direct } };
            Layout(clipped);
            Assert.NotNull(LayoutInformation.GetLayoutClip(direct));

            var hosted = new Path { Data = geometry };
            var body = new Grid { Width = W, Height = H, Children = { new Canvas { Children = { hosted } } } };
            Layout(body);
            Assert.Null(LayoutInformation.GetLayoutClip(hosted));
        });
    }

    [Theory]
    [InlineData(DockPosition.TopLeft)]
    [InlineData(DockPosition.TopRight)]
    public void Corner_shape_fills_the_screen_corner_and_rounds_the_inner_corner(DockPosition dock)
    {
        double w = 212, h = 44, r = 22;
        double inner = IslandShape.CornerInnerRadius(w, h);
        Assert.True(inner > r);

        var shape = IslandShape.Build(w, h, r, Flare, DockLayout.Edges(dock), inner);
        bool left = dock == DockPosition.TopLeft;
        double cornerX = left ? 0.5 : w - 0.5, innerX = left ? w - 3 : 3;

        Assert.True(shape.FillContains(new Point(cornerX, 0.5)));      // 螢幕角落是滿的
        Assert.False(shape.FillContains(new Point(innerX, h - 3)));    // 朝內的角是大圓弧
        Assert.True(shape.FillContains(new Point(cornerX, h + 4)));    // 沿側邊往下延伸
        Assert.True(shape.FillContains(new Point(left ? w + 4 : -4, 0.5))); // 沿上緣往外延伸
    }

    [Fact]
    public void Corner_inner_radius_is_large_when_compact_and_restrained_when_expanded()
    {
        Assert.InRange(IslandShape.CornerInnerRadius(212, 44), 28, 34);
        Assert.InRange(IslandShape.CornerInnerRadius(500, 200), 40, 56);
    }

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
    }

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }
}
