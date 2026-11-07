using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using TianMu.Island.Interop;

namespace TianMu.Island.Shell;

/// <summary>法陣盤上的一個扇區（宿主整理好的）。</summary>
internal sealed record RadialItem(string Label, string Caption, Action Invoke, Color? Tone);

/// <summary>
/// 法陣盤：長按天幕後在游標處展開的圓形功能選單。
///
/// 兩種用法：
///   · 按住不放：拖到扇區上放開就執行，回到陣眼放開就取消
///   · 長按後在陣眼上放開：盤留著，改用點的；點盤外或右鍵關閉
///
/// 透明、置頂、不搶焦點（打字的視窗不會被打斷）。外觀與「金線輪轉」展開動畫見 <see cref="RadialGeometry"/>。
/// </summary>
internal sealed class RadialMenuWindow : Window
{
    private const double Side = 400;                 // 視窗邊長（DIP），盤的直徑 300 再留出光暈
    private const double HoverPush = 7;              // 滑到的扇區往外推一點
    private static readonly TimeSpan StickyWithin = TimeSpan.FromMilliseconds(700);

    private static readonly Color Gold = Color.FromRgb(0xC8, 0xA8, 0x69);
    private static readonly Color GoldHi = Color.FromRgb(0xF0, 0xDC, 0xA8);
    private static readonly Color Moon = Color.FromRgb(0xEC, 0xE6, 0xD8);
    private static readonly Brush Face = Frozen(Color.FromRgb(0x1D, 0x17, 0x12));
    private static readonly Brush FaceHot = Frozen(Color.FromRgb(0x3A, 0x2C, 0x1A));
    private static readonly Brush Edge = Frozen(Color.FromArgb(0x47, 0xC8, 0xA8, 0x69));
    private static readonly Brush EdgeHot = Frozen(Gold);

    private readonly IReadOnlyList<RadialItem> _items;
    private readonly Point _c = new(Side / 2, Side / 2);
    private readonly List<SliceVisual> _slices = [];
    private readonly Path _outline;
    private readonly Grid _hub;
    private readonly TextBlock _hubTitle;
    private readonly TextBlock _hubCaption;
    private readonly DateTime _openedAt = DateTime.Now;
    private readonly bool _animate = SystemParameters.ClientAreaAnimation;
    private TimeSpan _animStart = TimeSpan.Zero;
    private int _hot = -1;
    private bool _leftHub;
    private bool _sticky;
    private bool _done;

    private sealed record SliceVisual(Canvas Group, Path Shape, TextBlock Label, TextBlock Caption, double Mid, ScaleTransform Scale, TranslateTransform Push);

    public RadialMenuWindow(IReadOnlyList<RadialItem> items)
    {
        _items = items;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = Height = Side;
        Title = "天幕島 · 法陣盤";

        var canvas = new Canvas { Width = Side, Height = Side };

        // 外圈金線（展開時先畫一圈）
        double ringR = RadialGeometry.OuterRadius + 6;
        _outline = new Path
        {
            Stroke = new SolidColorBrush(Gold),
            StrokeThickness = 1,
            Data = CircleFromTop(ringR),
            StrokeDashArray = new DoubleCollection { 0, 2 * Math.PI * ringR },
            Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Color = Gold, Opacity = 0.8 },
            IsHitTestVisible = false,
        };
        canvas.Children.Add(_outline);

        for (int i = 0; i < items.Count; i++)
        {
            var (a0, a1, mid) = RadialGeometry.SliceAngles(i, items.Count);
            var shape = new Path
            {
                Data = RadialGeometry.SlicePath(_c, a0, a1, RadialGeometry.HubRadius + 6, RadialGeometry.OuterRadius),
                Fill = Face,
                Stroke = Edge,
                StrokeThickness = 1,
            };
            var tone = items[i].Tone ?? Moon;
            var label = new TextBlock
            {
                Text = items[i].Label,
                FontFamily = (FontFamily)Application.Current.FindResource("SerifFont"),
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                Foreground = new SolidColorBrush(tone),
            };
            var caption = new TextBlock
            {
                Text = items[i].Caption,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(0x61, Moon.R, Moon.G, Moon.B)),
            };
            double lr = (RadialGeometry.HubRadius + RadialGeometry.OuterRadius) / 2 + 4;
            var anchor = new Point(_c.X + lr * Math.Cos(mid), _c.Y + lr * Math.Sin(mid));
            var labels = new StackPanel { IsHitTestVisible = false };
            label.HorizontalAlignment = caption.HorizontalAlignment = HorizontalAlignment.Center;
            labels.Children.Add(label);
            labels.Children.Add(caption);
            labels.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(labels, anchor.X - labels.DesiredSize.Width / 2);
            Canvas.SetTop(labels, anchor.Y - labels.DesiredSize.Height / 2);

            var scale = new ScaleTransform(1, 1, _c.X, _c.Y);
            var push = new TranslateTransform();
            var group = new Canvas { Width = Side, Height = Side, RenderTransform = new TransformGroup { Children = { scale, push } } };
            group.Children.Add(shape);
            group.Children.Add(labels);
            canvas.Children.Add(group);
            _slices.Add(new SliceVisual(group, shape, label, caption, mid, scale, push));
        }

        // 陣眼
        _hubTitle = new TextBlock
        {
            Text = "天幕",
            FontFamily = (FontFamily)Application.Current.FindResource("SerifFont"),
            FontWeight = FontWeights.Black,
            FontSize = 17,
            Foreground = new SolidColorBrush(Gold),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _hubCaption = new TextBlock
        {
            Text = "移回取消",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(0x61, Moon.R, Moon.G, Moon.B)),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var hubText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        hubText.Children.Add(_hubTitle);
        hubText.Children.Add(_hubCaption);
        var hubFill = new RadialGradientBrush(Color.FromRgb(0x2B, 0x21, 0x17), Color.FromRgb(0x0E, 0x0C, 0x0A)) { GradientOrigin = new Point(0.5, 0.4) };
        _hub = new Grid { Width = RadialGeometry.HubRadius * 2, Height = RadialGeometry.HubRadius * 2, IsHitTestVisible = false };
        _hub.Children.Add(new Ellipse { Fill = hubFill });
        _hub.Children.Add(new Ellipse { Margin = new Thickness(4), Stroke = new SolidColorBrush(Color.FromArgb(0x8C, Gold.R, Gold.G, Gold.B)), StrokeThickness = 1 });
        _hub.Children.Add(hubText);
        Canvas.SetLeft(_hub, _c.X - RadialGeometry.HubRadius);
        Canvas.SetTop(_hub, _c.Y - RadialGeometry.HubRadius);
        canvas.Children.Add(_hub);

        // 盤面下方的淡淡陰影，讓盤在亮色桌面上也看得清楚
        canvas.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 0, Color = Colors.Black, Opacity = 0.55, RenderingBias = RenderingBias.Performance };
        Content = canvas;

        SourceInitialized += (_, _) =>
        {
            nint hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.ToggleExStyle(hwnd, NativeMethods.WS_EX_TOOLWINDOW, true);
            NativeMethods.ToggleExStyle(hwnd, NativeMethods.WS_EX_NOACTIVATE, true);
        };
        Loaded += (_, _) =>
        {
            ApplyFrame(_animate ? 0 : double.MaxValue);
            if (_animate) CompositionTarget.Rendering += OnRendering;
        };
        Closed += (_, _) => CompositionTarget.Rendering -= OnRendering;

        // 點的模式
        MouseMove += (_, e) => { if (_sticky) Hover(e.GetPosition(this)); };
        MouseLeftButtonUp += (_, e) => { if (_sticky) Choose(e.GetPosition(this)); };
        MouseRightButtonUp += (_, _) => Finish(null);
        LostMouseCapture += (_, _) => { if (_sticky && !_done) Finish(null); };
    }

    /// <summary>以某個螢幕點（DIP）為中心顯示，並避開螢幕邊緣。</summary>
    public void ShowAt(Point centerDip, Rect workAreaDip)
    {
        double half = Side / 2;
        Left = Math.Clamp(centerDip.X - half, workAreaDip.Left - 40, workAreaDip.Right - Side + 40);
        Top = Math.Clamp(centerDip.Y - half, workAreaDip.Top - 40, workAreaDip.Bottom - Side + 40);
        Show();
        if (PresentationSource.FromVisual(this) is HwndSource source) NativeMethods.ReassertTopmost(source.Handle);
    }

    // ═══════════════════════ 按住拖曳的用法（由天幕轉送滑鼠位置） ═══════════════════════

    /// <summary>天幕仍抓著滑鼠時，轉送游標位置（實際像素）。</summary>
    public void Track(Point screenPx) => Hover(PointFromScreen(screenPx));

    /// <summary>
    /// 在天幕上放開滑鼠時呼叫。落在扇區上就執行；剛展開、還沒離開陣眼就放開，改成點的模式；其餘取消。
    /// </summary>
    public void Release(Point screenPx)
    {
        var p = PointFromScreen(screenPx);
        int hit = RadialGeometry.HitTest(p, _c, _items.Count);
        if (hit >= 0)
        {
            Finish(_items[hit]);
            return;
        }

        if (hit == -1 && !_leftHub && DateTime.Now - _openedAt < StickyWithin + RadialGeometry.OpenDuration)
        {
            // 長按後沒拖動就放開：盤留著，改用點的
            _sticky = true;
            _hubCaption.Text = "點選，盤外取消";
            Mouse.Capture(this, CaptureMode.SubTree);
            return;
        }

        Finish(null);
    }

    private void Choose(Point p)
    {
        int hit = RadialGeometry.HitTest(p, _c, _items.Count, slack: 0);
        Finish(hit >= 0 ? _items[hit] : null);
    }

    private void Hover(Point p)
    {
        int hit = RadialGeometry.HitTest(p, _c, _items.Count);
        if (hit != -1) _leftHub = true;
        if (hit == _hot) return;
        _hot = hit;

        for (int i = 0; i < _slices.Count; i++)
        {
            var s = _slices[i];
            bool hot = i == hit;
            s.Shape.Fill = hot ? FaceHot : Face;
            s.Shape.Stroke = hot ? EdgeHot : Edge;
            s.Push.X = hot ? Math.Cos(s.Mid) * HoverPush : 0;
            s.Push.Y = hot ? Math.Sin(s.Mid) * HoverPush : 0;
            s.Label.Foreground = hot ? new SolidColorBrush(GoldHi) : new SolidColorBrush(_items[i].Tone ?? Moon);
        }

        if (hit >= 0)
        {
            _hubTitle.Text = _items[hit].Label;
            _hubCaption.Text = _items[hit].Caption;
        }
        else
        {
            _hubTitle.Text = "天幕";
            _hubCaption.Text = _sticky ? "點選，盤外取消" : "移回取消";
        }
    }

    /// <summary>關閉並（若有選中）執行。只會發生一次。</summary>
    private void Finish(RadialItem? chosen)
    {
        if (_done) return;
        _done = true;
        if (Mouse.Captured == this) ReleaseMouseCapture();
        Close();
        if (chosen is null) return;
        try { chosen.Invoke(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[法陣盤] {chosen.Label} 執行失敗：{ex.Message}"); }
    }

    // ═══════════════════════ 展開動畫 ═══════════════════════

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (_animStart == TimeSpan.Zero) _animStart = now;
        double ms = (now - _animStart).TotalMilliseconds;
        ApplyFrame(ms);
        if (ms > RadialGeometry.OpenDuration.TotalMilliseconds) CompositionTarget.Rendering -= OnRendering;
    }

    private void ApplyFrame(double ms)
    {
        double ringR = RadialGeometry.OuterRadius + 6, len = 2 * Math.PI * ringR;
        double outline = RadialGeometry.OutlineProgress(ms);
        _outline.StrokeDashArray = new DoubleCollection { len * outline, len };
        _outline.Opacity = ms > 900 ? Math.Max(0.25, 0.85 - (ms - 900) / 600) : 0.85;
        _hub.Opacity = RadialGeometry.HubOpacity(ms);

        for (int i = 0; i < _slices.Count; i++)
        {
            var (scale, opacity, text) = RadialGeometry.Slice(ms, i);
            var s = _slices[i];
            s.Scale.ScaleX = s.Scale.ScaleY = scale;
            s.Group.Opacity = opacity;
            s.Label.Opacity = s.Caption.Opacity = text;
        }
    }

    /// <summary>從正上方開始順時針的一整圈（讓金線從頂端開始畫）。</summary>
    private Geometry CircleFromTop(double r)
    {
        var top = new Point(_c.X, _c.Y - r);
        var bottom = new Point(_c.X, _c.Y + r);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(top, isFilled: false, isClosed: false);
            ctx.ArcTo(bottom, new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            ctx.ArcTo(top, new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
        }
        g.Freeze();
        return g;
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
