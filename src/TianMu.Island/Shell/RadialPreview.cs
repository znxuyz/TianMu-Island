using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TianMu.Island.Shell;

/// <summary>
/// 設定面板裡的法陣盤小樣：用跟真正法陣盤同一套 <see cref="RadialGeometry"/> 播「金線輪轉」，
/// 扇區數量一變就重播一次；點一下也會重播。
/// </summary>
public sealed class RadialPreview : FrameworkElement
{
    public static readonly DependencyProperty LabelsProperty = DependencyProperty.Register(
        nameof(Labels), typeof(IReadOnlyList<string>), typeof(RadialPreview),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((RadialPreview)d).Replay()));

    private static readonly Color Gold = Color.FromRgb(0xC8, 0xA8, 0x69);
    private static readonly Color Moon = Color.FromRgb(0xEC, 0xE6, 0xD8);
    private static readonly Brush Face = Frozen(new SolidColorBrush(Color.FromRgb(0x1D, 0x17, 0x12)));
    private static readonly Pen Edge = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x47, Gold.R, Gold.G, Gold.B))), 1));
    private static readonly Brush HubFill = Frozen(new RadialGradientBrush(Color.FromRgb(0x2B, 0x21, 0x17), Color.FromRgb(0x0E, 0x0C, 0x0A)) { GradientOrigin = new Point(0.5, 0.4) });
    private static readonly Pen HubRing = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x8C, Gold.R, Gold.G, Gold.B))), 1));

    private static readonly Brush Plate = Frozen(new RadialGradientBrush(Color.FromRgb(0xF1, 0xEB, 0xDD), Color.FromRgb(0xDD, 0xD3, 0xBE)) { GradientOrigin = new Point(0.5, 0.35) });
    private static readonly Pen PlateEdge = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xB3, Gold.R, Gold.G, Gold.B))), 1.5));

    private readonly bool _animate = SystemParameters.ClientAreaAnimation;
    private TimeSpan? _start;
    private double _ms = RadialGeometry.SweepMs + 1000;
    private bool _hooked;

    public RadialPreview()
    {
        Cursor = Cursors.Hand;
        ToolTip = "點一下重播展開動畫";
        Loaded += (_, _) => Replay();
        Unloaded += (_, _) => Unhook();
    }

    public IReadOnlyList<string>? Labels
    {
        get => (IReadOnlyList<string>?)GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Replay();
        e.Handled = true;
    }

    /// <summary>從頭播一次展開動畫（系統關閉動畫時直接畫完成的樣子）。</summary>
    public void Replay()
    {
        if (!_animate || !IsLoaded) { _ms = RadialGeometry.SweepMs + 1000; InvalidateVisual(); return; }
        _start = null;
        _ms = 0;
        if (!_hooked) { CompositionTarget.Rendering += OnRendering; _hooked = true; }
        InvalidateVisual();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        _start ??= now;
        _ms = (now - _start.Value).TotalMilliseconds;
        if (_ms > RadialGeometry.OpenDuration.TotalMilliseconds + 800) Unhook();
        InvalidateVisual();
    }

    private void Unhook()
    {
        if (!_hooked) return;
        CompositionTarget.Rendering -= OnRendering;
        _hooked = false;
    }

    // 不要求超過版面給的空間：要求得比給的大時，WPF 會照要求的大小畫、再把超出的部分裁掉（之前小盤被切一半就是這樣）
    protected override Size MeasureOverride(Size available) =>
        new(double.IsInfinity(available.Width) ? 220 : Math.Min(220, available.Width),
            double.IsInfinity(available.Height) ? 220 : Math.Min(220, available.Height));

    protected override void OnRender(DrawingContext dc)
    {
        var labels = Labels;
        if (labels is null || labels.Count == 0) return;
        int n = labels.Count;
        double ms = _ms;

        double ringR = RadialGeometry.OuterRadius + 6;
        double k = Math.Min(ActualWidth, ActualHeight) / (2 * (RadialGeometry.OuterRadius + 16));
        var c = new Point(ActualWidth / 2 / k, ActualHeight / 2 / k);
        dc.PushTransform(new ScaleTransform(k, k));

        // 淺色底盤（與真正的法陣盤相同）
        double plateR = RadialGeometry.OuterRadius + 14;
        dc.PushOpacity(RadialGeometry.HubOpacity(ms));
        dc.DrawEllipse(Plate, PlateEdge, c, plateR, plateR);
        dc.Pop();

        // 扇區：只畫到金線已經掃過的角度，從陣眼往外長
        double r1 = RadialGeometry.HubRadius + 6;
        var serif = Application.Current.TryFindResource("SerifFont") as FontFamily ?? new FontFamily("serif");
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (int i = 0; i < n; i++)
        {
            var (a0, _, mid) = RadialGeometry.SliceAngles(i, n);
            var (edge, grow, text) = RadialGeometry.Slice(ms, i, n);
            if (edge <= a0 + 0.0005) continue;
            double r2 = r1 + (RadialGeometry.OuterRadius - r1) * Math.Min(grow, 1.06);
            dc.DrawGeometry(Face, Edge, RadialGeometry.SlicePath(c, a0, edge, r1, r2));
            if (text <= 0) continue;

            var ft = new FormattedText(labels[i], CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(serif, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), n > 8 ? 17 : 19,
                new SolidColorBrush(Color.FromArgb((byte)(255 * text), Moon.R, Moon.G, Moon.B)), dpi);
            double lr = (RadialGeometry.HubRadius + RadialGeometry.OuterRadius) / 2 + 4;
            dc.DrawText(ft, new Point(c.X + lr * Math.Cos(mid) - ft.Width / 2, c.Y + lr * Math.Sin(mid) - ft.Height / 2));
        }

        // 金線與筆尖
        double f = RadialGeometry.OutlineProgress(ms);
        double fade = ms > RadialGeometry.SweepMs + 300 ? Math.Max(0.3, 0.9 - (ms - RadialGeometry.SweepMs - 300) / 700) : 0.9;
        var gold = new Pen(new SolidColorBrush(Color.FromArgb((byte)(255 * fade), Gold.R, Gold.G, Gold.B)), 1.6);
        double s0 = RadialGeometry.StartAngle(n), s1 = RadialGeometry.SweepAngle(ms, n);
        if (f >= 1) dc.DrawEllipse(null, gold, c, ringR, ringR);
        else if (f > 0)
        {
            dc.DrawGeometry(null, gold, Arc(c, ringR, s0, s1));
            var tip = new Point(c.X + ringR * Math.Cos(s1), c.Y + ringR * Math.Sin(s1));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xF0, 0xD9, 0xA0)), null, tip, 4, 4);
        }

        // 陣眼
        double hub = RadialGeometry.HubOpacity(ms);
        dc.PushOpacity(hub);
        dc.DrawEllipse(HubFill, null, c, RadialGeometry.HubRadius, RadialGeometry.HubRadius);
        dc.DrawEllipse(null, HubRing, c, RadialGeometry.HubRadius - 4, RadialGeometry.HubRadius - 4);
        var count = new FormattedText($"{n} 格", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(serif, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 20, new SolidColorBrush(Gold), dpi);
        dc.DrawText(count, new Point(c.X - count.Width / 2, c.Y - count.Height / 2));
        dc.Pop();

        dc.Pop();
    }

    private static Geometry Arc(Point c, double r, double a0, double a1)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0)), false, false);
            ctx.ArcTo(new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1)), new Size(r, r), 0, a1 - a0 > Math.PI,
                SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
