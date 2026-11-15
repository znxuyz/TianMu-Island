using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using TianMu.Modules.Wenfang.Shared;

namespace TianMu.Modules.Wenfang.Slides;

/// <summary>把一張投影片畫成 WPF 元素（編輯畫面、縮圖、放映共用同一套畫法，看到的就是放映的樣子）。</summary>
internal static class SlideView
{
    public const double InsetX = 9.6, InsetY = 4.8; // PowerPoint 預設文字邊界 0.1 吋／0.05 吋

    public static Canvas Render(Deck deck, Slide slide, bool editing, IDictionary<SlideItem, FrameworkElement>? map = null)
    {
        var canvas = new Canvas { Width = deck.Width, Height = deck.Height, Background = Brush(deck.BackgroundOf(slide)), ClipToBounds = true };
        foreach (var item in slide.Items)
        {
            var el = RenderItem(deck, item, editing);
            Place(el, item);
            canvas.Children.Add(el);
            if (map is not null) map[item] = el;
        }
        return canvas;
    }

    public static void Place(FrameworkElement el, SlideItem item)
    {
        Canvas.SetLeft(el, item.X);
        Canvas.SetTop(el, item.Y);
        el.Width = Math.Max(1, item.W);
        el.Height = Math.Max(1, item.H);
    }

    public static FrameworkElement RenderItem(Deck deck, SlideItem item, bool editing)
    {
        if (item.Kind == ItemKind.Image)
        {
            var src = Bitmap(item.ImageBytes);
            return src is null
                ? new Border { Background = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)), Child = new TextBlock { Text = "（無法顯示的圖片）", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } }
                : new Image { Source = src, Stretch = Stretch.Fill };
        }

        var grid = new Grid();
        if (item.Kind == ItemKind.Shape)
        {
            Shape shape = item.Geometry == "ellipse" ? new Ellipse() : new Rectangle
            {
                RadiusX = item.Geometry == "roundRect" ? Math.Min(item.W, item.H) * 0.1667 : 0,
                RadiusY = item.Geometry == "roundRect" ? Math.Min(item.W, item.H) * 0.1667 : 0,
            };
            shape.Fill = Brush(deck.FillOf(item));
            grid.Children.Add(shape);
        }
        else if (editing && item.Role != ItemRole.None && item.Text.Length == 0)
        {
            grid.Children.Add(new Rectangle { Stroke = new SolidColorBrush(Color.FromArgb(0x80, 0x99, 0x99, 0x99)), StrokeDashArray = [4, 3], StrokeThickness = 1.2 });
        }

        bool hint = editing && item.Text.Length == 0 && item.Kind == ItemKind.Text;
        string text = hint ? Deck.Hint(item) : item.Text;
        if (text.Length > 0) grid.Children.Add(TextPanel(deck, item, text, hint));
        return grid;
    }

    private static FrameworkElement TextPanel(Deck deck, SlideItem item, string text, bool hint)
    {
        double px = item.FontSize * 96 / 72;
        var color = hint ? new SolidColorBrush(Color.FromArgb(0x90, 0x88, 0x88, 0x88)) : Brush(deck.ColorOf(item));
        var font = new FontFamily(deck.FontOf(item) + ", Microsoft JhengHei, Segoe UI");
        var panel = new StackPanel
        {
            Margin = new Thickness(InsetX, InsetY, InsetX, InsetY),
            VerticalAlignment = item.Anchor switch { VAnchor.Middle => VerticalAlignment.Center, VAnchor.Bottom => VerticalAlignment.Bottom, _ => VerticalAlignment.Top },
        };
        var align = item.Align switch { HAlign.Center => TextAlignment.Center, HAlign.Right => TextAlignment.Right, _ => TextAlignment.Left };
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var tb = new TextBlock
            {
                Text = line.Length == 0 ? " " : line, FontSize = px, FontFamily = font, Foreground = color, TextWrapping = TextWrapping.Wrap, TextAlignment = align,
                FontWeight = item.Bold ? FontWeights.Bold : FontWeights.Normal, FontStyle = item.Italic || hint ? FontStyles.Italic : FontStyles.Normal,
                LineHeight = px * 1.2, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            };
            if (item.Bullets && line.Length > 0 && !hint)
            {
                var row = new DockPanel { Margin = new Thickness(0, px * 0.25, 0, 0) };
                var dot = new TextBlock { Text = "•", FontSize = px, Foreground = color, Width = px * 1.0, FontFamily = font, LineHeight = px * 1.2, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);
                row.Children.Add(tb);
                panel.Children.Add(row);
            }
            else panel.Children.Add(tb);
        }
        return panel;
    }

    public static SolidColorBrush Brush(string? hex)
    {
        var c = Ui.FromHex(hex) ?? Colors.White;
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public static BitmapSource? Bitmap(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception) { return null; }
    }

    /// <summary>縮圖：整張投影片縮進指定寬度。</summary>
    public static FrameworkElement Thumbnail(Deck deck, Slide slide, double width) =>
        new Viewbox { Width = width, Height = width * deck.Height / deck.Width, Stretch = Stretch.Uniform, Child = Render(deck, slide, false) };
}

/// <summary>全螢幕放映：→／空白鍵／滑鼠左鍵下一張，←／右鍵上一張，Esc 結束。</summary>
internal sealed class SlideShowWindow : Window
{
    private readonly Deck _deck;
    private readonly Viewbox _view = new() { Stretch = Stretch.Uniform };
    private int _index;

    public SlideShowWindow(Deck deck, int start, Window? owner)
    {
        _deck = deck;
        _index = Math.Clamp(start, 0, Math.Max(0, deck.Slides.Count - 1));
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Black;
        Topmost = true;
        Cursor = Cursors.None;
        Title = "畫卷放映";
        Content = _view;
        if (owner is not null)
        {
            // 在編輯視窗所在的螢幕放映
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = owner.Left + 40;
            Top = owner.Top + 40;
            Width = 400;
            Height = 300;
        }
        Loaded += (_, _) => { WindowState = WindowState.Maximized; Show(_index); Activate(); Focus(); };
        MouseMove += (_, _) => Cursor = Cursors.Arrow;
    }

    private void Show(int index)
    {
        if (index < 0) return;
        _index = index;
        if (index >= _deck.Slides.Count)
        {
            _view.Child = new Grid
            {
                Width = _deck.Width, Height = _deck.Height, Background = Brushes.Black,
                Children = { new TextBlock { Text = "放映結束，按一下離開", Foreground = Brushes.Gray, FontSize = 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 40, 0, 0) } },
            };
            return;
        }
        var slide = SlideView.Render(_deck, _deck.Slides[index], editing: false);
        _view.Child = slide;
        slide.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(220)));
    }

    private void Next() { if (_index >= _deck.Slides.Count) Close(); else Show(_index + 1); }
    private void Prev() => Show(Math.Max(0, _index - 1));

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Right or Key.Down or Key.Space or Key.PageDown or Key.Enter or Key.N: Next(); break;
            case Key.Left or Key.Up or Key.PageUp or Key.Back or Key.P: Prev(); break;
            case Key.Home: Show(0); break;
            case Key.End: Show(_deck.Slides.Count - 1); break;
            case Key.Escape: Close(); break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { Next(); e.Handled = true; }
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e) { Prev(); e.Handled = true; }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (e.Delta < 0) Next(); else Prev();
        e.Handled = true;
    }
}
