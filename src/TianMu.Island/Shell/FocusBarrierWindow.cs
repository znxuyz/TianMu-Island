using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using TianMu.Island.Interop;

namespace TianMu.Island.Shell;

/// <summary>
/// 專注結界：番茄鐘專注時，沿著螢幕四邊亮起一圈由外往內淡去的金光，緩緩呼吸。
/// 視窗完全透明、滑鼠穿透、不搶焦點，不會妨礙任何操作。
/// </summary>
internal sealed class FocusBarrierWindow : Window
{
    private const double GlowDepth = 34;          // 光由邊緣往內延伸的距離（DIP）
    private static readonly Color Gold = Color.FromRgb(0xC8, 0xA8, 0x69);

    public FocusBarrierWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        Focusable = false;
        Title = "天幕島 · 專注結界";

        var grid = new Grid { IsHitTestVisible = false, Opacity = 0 };
        grid.Children.Add(Edge(VerticalAlignment.Top, HorizontalAlignment.Stretch, new Point(0.5, 0), new Point(0.5, 1)));
        grid.Children.Add(Edge(VerticalAlignment.Bottom, HorizontalAlignment.Stretch, new Point(0.5, 1), new Point(0.5, 0)));
        grid.Children.Add(Edge(VerticalAlignment.Stretch, HorizontalAlignment.Left, new Point(0, 0.5), new Point(1, 0.5)));
        grid.Children.Add(Edge(VerticalAlignment.Stretch, HorizontalAlignment.Right, new Point(1, 0.5), new Point(0, 0.5)));
        Content = grid;

        SourceInitialized += (_, _) =>
        {
            nint hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.ToggleExStyle(hwnd, NativeMethods.WS_EX_TRANSPARENT, true);
            NativeMethods.ToggleExStyle(hwnd, NativeMethods.WS_EX_TOOLWINDOW, true);
            NativeMethods.ToggleExStyle(hwnd, NativeMethods.WS_EX_NOACTIVATE, true);
        };

        Loaded += (_, _) =>
        {
            // 先淡入，再開始呼吸（使用者關閉動畫效果時就靜靜亮著）
            if (!SystemParameters.ClientAreaAnimation)
            {
                grid.Opacity = 0.6;
                return;
            }
            var appear = new DoubleAnimation(0, 0.75, TimeSpan.FromSeconds(1.2));
            appear.Completed += (_, _) =>
                grid.BeginAnimation(OpacityProperty, new DoubleAnimation(0.75, 0.3, TimeSpan.FromSeconds(3))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
            grid.BeginAnimation(OpacityProperty, appear);
        };
    }

    /// <summary>蓋住某個螢幕（DIP 座標）。</summary>
    public void Cover(Rect bounds)
    {
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    private static Rectangle Edge(VerticalAlignment v, HorizontalAlignment h, Point start, Point end)
    {
        var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x80, Gold.R, Gold.G, Gold.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x26, Gold.R, Gold.G, Gold.B), 0.35));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, Gold.R, Gold.G, Gold.B), 1));
        brush.Freeze();

        bool horizontal = h == HorizontalAlignment.Stretch;
        return new Rectangle
        {
            Fill = brush,
            VerticalAlignment = v,
            HorizontalAlignment = h,
            Height = horizontal ? GlowDepth : double.NaN,
            Width = horizontal ? double.NaN : GlowDepth,
            IsHitTestVisible = false,
        };
    }
}
