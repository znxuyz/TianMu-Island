using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace TianMu.Modules.Office.Views;

/// <summary>
/// 照妖鏡的框選畫面：把剛才擷取的螢幕畫面蓋滿整個螢幕（看起來像畫面被定住），
/// 暗下來，拖出一個框，框裡亮起並描上金邊。放開就交出框住的那一塊；Esc 或右鍵取消。
/// </summary>
internal sealed class SnipWindow : Window
{
    private static readonly Color Gold = Color.FromRgb(0xC8, 0xA8, 0x69);

    private readonly BitmapSource _shot;
    private readonly Int32Rect _bounds;
    private readonly Path _shade;
    private readonly Rectangle _frame;
    private readonly Border _hint;
    private Point? _start;

    /// <summary>框好之後交出的那一塊（實際像素）；取消時是 null。</summary>
    public CroppedBitmap? Result { get; private set; }

    public SnipWindow(BitmapSource shot, Int32Rect bounds)
    {
        _shot = shot;
        _bounds = bounds;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        Title = "照妖鏡";
        Background = Brushes.Black;

        var canvas = new Grid();
        canvas.Children.Add(new Image { Source = shot, Stretch = Stretch.Fill });
        _shade = new Path { Fill = new SolidColorBrush(Color.FromArgb(0x99, 0x0E, 0x0C, 0x0A)), IsHitTestVisible = false };
        canvas.Children.Add(_shade);
        var overlay = new Canvas { IsHitTestVisible = false };
        _frame = new Rectangle
        {
            Stroke = new SolidColorBrush(Gold),
            StrokeThickness = 1.5,
            Visibility = Visibility.Collapsed,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 0, Color = Gold, Opacity = 0.9 },
        };
        overlay.Children.Add(_frame);
        canvas.Children.Add(overlay);

        _hint = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 56, 0, 0),
            Padding = new Thickness(18, 8, 18, 8),
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x0E, 0x0C, 0x0A)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, Gold.R, Gold.G, Gold.B)),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "照妖鏡 · 框選要取字的範圍　Esc 取消",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0xEC, 0xE6, 0xD8)),
            },
        };
        canvas.Children.Add(_hint);
        Content = canvas;

        SourceInitialized += (_, _) =>
        {
            // 用實際像素放到那個螢幕上，避開不同螢幕縮放比例的換算
            var hwnd = new WindowInteropHelper(this).Handle;
            ScreenCapture.SetWindowPos(hwnd, new nint(-1) /* HWND_TOPMOST */, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, 0x0040 /* SWP_SHOWWINDOW */);
        };
        Loaded += (_, _) => { Activate(); Focus(); UpdateShade(null); };

        MouseLeftButtonDown += (_, e) => { _start = e.GetPosition(this); CaptureMouse(); _hint.Visibility = Visibility.Collapsed; };
        MouseMove += (_, e) => { if (_start is { } s) UpdateShade(new Rect(s, e.GetPosition(this))); };
        MouseLeftButtonUp += (_, e) =>
        {
            if (_start is not { } s) return;
            ReleaseMouseCapture();
            Finish(new Rect(s, e.GetPosition(this)));
        };
        MouseRightButtonUp += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Deactivated += (_, _) => { if (IsLoaded && _start is null) Close(); };
    }

    private void UpdateShade(Rect? selection)
    {
        var all = new RectangleGeometry(new Rect(0, 0, Math.Max(1, ActualWidth), Math.Max(1, ActualHeight)));
        if (selection is not { } r || r.Width < 1 || r.Height < 1)
        {
            _shade.Data = all;
            _frame.Visibility = Visibility.Collapsed;
            return;
        }
        _shade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, all, new RectangleGeometry(r));
        _frame.Visibility = Visibility.Visible;
        Canvas.SetLeft(_frame, r.X);
        Canvas.SetTop(_frame, r.Y);
        _frame.Width = r.Width;
        _frame.Height = r.Height;
    }

    private void Finish(Rect r)
    {
        if (r.Width < 6 || r.Height < 6) { Close(); return; } // 只是點了一下
        double sx = _shot.PixelWidth / Math.Max(1, ActualWidth), sy = _shot.PixelHeight / Math.Max(1, ActualHeight);
        int x = (int)Math.Clamp(r.X * sx, 0, _shot.PixelWidth - 1);
        int y = (int)Math.Clamp(r.Y * sy, 0, _shot.PixelHeight - 1);
        int w = (int)Math.Clamp(r.Width * sx, 1, _shot.PixelWidth - x);
        int h = (int)Math.Clamp(r.Height * sy, 1, _shot.PixelHeight - y);
        Result = new CroppedBitmap(_shot, new Int32Rect(x, y, w, h));
        Result.Freeze();
        Close();
    }
}
