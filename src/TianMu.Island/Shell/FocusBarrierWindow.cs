using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using TianMu.Island.Interop;

namespace TianMu.Island.Shell;

/// <summary>
/// 螢幕四邊的光框，有兩種用法：
///   · 專注結界（<see cref="FocusBarrierWindow()"/>）：專注時沿四邊亮起淡淡的金光，緩緩呼吸；
///     完全透明、滑鼠穿透、不搶焦點，不妨礙任何操作。
///   · 敲鐘（<see cref="Bell"/>）：時間到時光框強烈閃動，上方寫著一行字；點螢幕任何地方
///     （或按 Esc、或 30 秒後）才關閉——這是不能錯過的提醒。
/// </summary>
internal sealed class FocusBarrierWindow : Window
{
    private static readonly TimeSpan BellTimeout = TimeSpan.FromSeconds(30);
    private static readonly Color Gold = Color.FromRgb(0xC8, 0xA8, 0x69);

    private readonly bool _bell;
    private readonly Grid _glow;
    private DispatcherTimer? _timeout;

    /// <summary>專注結界。</summary>
    public FocusBarrierWindow() : this(bell: false, Gold, message: null) { }

    /// <summary>敲鐘：點一下才關。</summary>
    public static FocusBarrierWindow Bell(string message, Color color) => new(bell: true, color, message);

    private FocusBarrierWindow(bool bell, Color color, string? message)
    {
        _bell = bell;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        // 敲鐘時要接得到點擊：幾乎全透明但不是完全透明（完全透明的地方滑鼠會穿過去）
        Background = bell ? new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0)) : Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = bell;
        ResizeMode = ResizeMode.NoResize;
        Title = bell ? "天幕島 · 時辰已到" : "天幕島 · 專注結界";

        double depth = bell ? 56 : 34;
        byte strong = bell ? (byte)0xD0 : (byte)0x80;
        _glow = new Grid { IsHitTestVisible = false, Opacity = 0 };
        _glow.Children.Add(Edge(VerticalAlignment.Top, HorizontalAlignment.Stretch, new Point(0.5, 0), new Point(0.5, 1), color, strong, depth));
        _glow.Children.Add(Edge(VerticalAlignment.Bottom, HorizontalAlignment.Stretch, new Point(0.5, 1), new Point(0.5, 0), color, strong, depth));
        _glow.Children.Add(Edge(VerticalAlignment.Stretch, HorizontalAlignment.Left, new Point(0, 0.5), new Point(1, 0.5), color, strong, depth));
        _glow.Children.Add(Edge(VerticalAlignment.Stretch, HorizontalAlignment.Right, new Point(1, 0.5), new Point(0, 0.5), color, strong, depth));

        var root = new Grid();
        root.Children.Add(_glow);
        if (bell && !string.IsNullOrWhiteSpace(message)) root.Children.Add(Caption(message, color));
        Content = root;

        SourceInitialized += (_, _) =>
        {
            nint hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.ToggleExStyle(hwnd, NativeMethods.WS_EX_TOOLWINDOW, true);
            if (!bell)
            {
                NativeMethods.ToggleExStyle(hwnd, NativeMethods.WS_EX_TRANSPARENT, true);
                NativeMethods.ToggleExStyle(hwnd, NativeMethods.WS_EX_NOACTIVATE, true);
            }
        };

        Loaded += (_, _) => Animate();

        if (bell)
        {
            MouseDown += (_, _) => Close();
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
            _timeout = new DispatcherTimer { Interval = BellTimeout };
            _timeout.Tick += (_, _) => Close();
            _timeout.Start();
            Closed += (_, _) => _timeout?.Stop();
        }
    }

    /// <summary>蓋住某個螢幕（DIP 座標）。</summary>
    public void Cover(Rect bounds)
    {
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    private void Animate()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            _glow.Opacity = _bell ? 0.9 : 0.6; // 使用者關閉動畫效果時就靜靜亮著
            return;
        }

        // 結界：慢慢淡入後緩緩呼吸；敲鐘：快速而明顯地閃動
        var (peak, low, period) = _bell ? (1.0, 0.35, 0.7) : (0.75, 0.3, 3.0);
        var appear = new DoubleAnimation(0, peak, TimeSpan.FromSeconds(_bell ? 0.25 : 1.2));
        appear.Completed += (_, _) =>
            _glow.BeginAnimation(OpacityProperty, new DoubleAnimation(peak, low, TimeSpan.FromSeconds(period))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        _glow.BeginAnimation(OpacityProperty, appear);
    }

    private static Rectangle Edge(VerticalAlignment v, HorizontalAlignment h, Point start, Point end, Color c, byte strong, double depth)
    {
        var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(strong, c.R, c.G, c.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(strong * 0.3), c.R, c.G, c.B), 0.35));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, c.R, c.G, c.B), 1));
        brush.Freeze();

        bool horizontal = h == HorizontalAlignment.Stretch;
        return new Rectangle
        {
            Fill = brush,
            VerticalAlignment = v,
            HorizontalAlignment = h,
            Height = horizontal ? depth : double.NaN,
            Width = horizontal ? double.NaN : depth,
            IsHitTestVisible = false,
        };
    }

    /// <summary>上方的一行字與「點任意處關閉」。</summary>
    private static UIElement Caption(string message, Color color)
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 120, 0, 0),
            IsHitTestVisible = false,
        };
        var card = new Border
        {
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(28, 14, 28, 14),
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x0E, 0x0C, 0x0A)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, color.R, color.G, color.B)),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 0, Color = color, Opacity = 0.6 },
        };
        var lines = new StackPanel();
        lines.Children.Add(new TextBlock
        {
            Text = message,
            FontFamily = (FontFamily)Application.Current.FindResource("SerifFont"),
            FontWeight = FontWeights.Bold,
            FontSize = 22,
            Foreground = new SolidColorBrush(color),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        lines.Children.Add(new TextBlock
        {
            Text = "點任意處關閉",
            FontSize = 12,
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        card.Child = lines;
        panel.Children.Add(card);
        return panel;
    }
}
