using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TianMu.Core;
using TianMu.Island.Physics;
using TianMu.Island.Shell;

namespace TianMu.Island;

/// <summary>
/// 天幕本體的第六部分：
///   · 水滴融合：同時有兩個常駐異象（例如音樂＋番茄鐘）時，天幕像水滴一樣分出一顆小圓顯示較早的那一個；
///     分開與合回時，中間有一段會拉長、變細、斷開的水滴頸
///   · 檔案拖放：把檔案拖到天幕上，交給法器處理（例如乾坤袋）
///   · 狀態燈：右上角的小圖示（例如麥克風、相機使用中）
/// </summary>
public partial class IslandWindow
{
    private const double SatelliteGap = 10;

    private readonly Spring _goo = new(0, stiffness: 140, damping: 11);   // ζ ≈ 0.46：分開時會 Q 彈地晃一下
    private IslandActivity? _satelliteActivity;
    private readonly List<(string ModuleId, string Hint, Action<IReadOnlyList<string>> OnDrop)> _dropHandlers = [];
    private readonly Dictionary<string, IslandIndicator> _indicators = [];

    // ═══════════════════════ 水滴融合 ═══════════════════════

    /// <summary>決定要不要分出小圓（在 Refresh 時呼叫）。</summary>
    private void UpdateSatellite()
    {
        var current = CurrentAmbient;
        var candidate = !IsExpanded && !_dragging && !_dormant && _ambient.Count >= 2
            ? _ambient.LastOrDefault(a => !ReferenceEquals(a, current))
            : null;
        if (candidate is not null)
        {
            _satelliteActivity = candidate;
            var badge = candidate.BadgeView as UIElement;
            if (!ReferenceEquals(SatelliteHost.Content, badge)) SatelliteHost.Content = badge;
            _goo.Target = 1;
        }
        else
        {
            _goo.Target = 0; // 合回去；內容等完全合回後才清掉
        }
        StartAnimation();
    }

    /// <summary>由彈簧動畫迴圈每一幀呼叫。回傳是否已靜止。</summary>
    private bool StepGoo(double dt)
    {
        _goo.Step(dt);
        ApplyGoo();
        return _goo.IsAtRest;
    }

    private void ApplyGoo()
    {
        double t = _goo.Value;
        if (t <= 0.002 && _goo.Target == 0)
        {
            Satellite.Visibility = Visibility.Collapsed;
            GooNeck.Data = null;
            SatelliteHost.Content = null;
            _satelliteActivity = null;
            return;
        }

        bool vertical = IsVertical;
        double w = Capsule.ActualWidth, h = Capsule.ActualHeight;
        if (w <= 0 || h <= 0) return;

        double r1 = (vertical ? w : h) / 2;
        double r2 = r1 * 0.94;

        // 分身從天幕「遠離邊緣」的那一端長出來
        Vector direction;
        Point endCenter;
        if (vertical)
        {
            direction = new Vector(0, 1);
            endCenter = new Point(w / 2, h - r1);
        }
        else if (_dock == DockPosition.TopRight)
        {
            direction = new Vector(-1, 0);
            endCenter = new Point(r1, h / 2);
        }
        else
        {
            direction = new Vector(1, 0);
            endCenter = new Point(w - r1, h / 2);
        }

        Point c1 = Capsule.TranslatePoint(endCenter, GooLayer);
        Point c2 = c1 + direction * (t * (r1 + SatelliteGap + r2));

        Satellite.Width = Satellite.Height = r2 * 2;
        Satellite.CornerRadius = new CornerRadius(r2);
        Canvas.SetLeft(Satellite, c2.X - r2);
        Canvas.SetTop(Satellite, c2.Y - r2);
        Satellite.Visibility = Visibility.Visible;
        SatelliteHost.Opacity = Math.Clamp((t - 0.55) / 0.45, 0, 1);

        GooNeck.Data = Metaball.Connect(c1, r1, c2, r2) is { } neck ? NeckGeometry(neck) : null;
    }

    private static Geometry NeckGeometry(MetaballNeck n)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(n.P1, isFilled: true, isClosed: true);
            ctx.BezierTo(n.H1, n.H3, n.P3, true, true);
            ctx.LineTo(n.P4, true, false);
            ctx.BezierTo(n.H4, n.H2, n.P2, true, true);
        }
        g.Freeze();
        return g;
    }

    /// <summary>點分身：展開並切到它的畫面。</summary>
    private void Satellite_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_satelliteActivity is null) return;
        SelectPage(_satelliteActivity.ModuleId);
        _userExpanded = true;
        Refresh();
        e.Handled = true;
    }

    // ═══════════════════════ 檔案拖放 ═══════════════════════

    public void AddFileDropHandler(string moduleId, string hint, Action<IReadOnlyList<string>> onDrop) =>
        Dispatcher.InvokeAsync(() =>
        {
            _dropHandlers.RemoveAll(d => d.ModuleId == moduleId);
            _dropHandlers.Add((moduleId, hint, onDrop));
        });

    private bool CanAcceptFiles(DragEventArgs e) =>
        _dropHandlers.Count > 0 && e.Data.GetDataPresent(DataFormats.FileDrop);

    private void Capsule_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = CanAcceptFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
        if (e.Effects != DragDropEffects.None && !_dropping)
        {
            DropHintText.Text = _dropHandlers[^1].Hint;
            DropHint.Visibility = Visibility.Visible;
            DropHintContent.Opacity = 1;
            _hovering = true;
            _dropping = true; // 天幕撐到中形，讓漩渦有地方轉
            OpenVortex();
            UpdateTargets();
        }
        e.Handled = true;
    }

    private void Capsule_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = CanAcceptFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Capsule_DragLeave(object sender, DragEventArgs e)
    {
        // 在膠囊內的子元素之間移動也會觸發，確認真的離開了才收起提示
        var p = e.GetPosition(Capsule);
        if (p.X >= 0 && p.Y >= 0 && p.X <= Capsule.ActualWidth && p.Y <= Capsule.ActualHeight) return;
        EndDragHint();
    }

    private void Capsule_Drop(object sender, DragEventArgs e)
    {
        if (!CanAcceptFiles(e) || e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
        {
            EndDragHint();
            return;
        }

        // 漩渦一收、金光一閃，檔案就進了袋子
        var handler = _dropHandlers[^1];
        SwallowVortex(() =>
        {
            EndDragHint();
            handler.OnDrop(files);
        });
        PlayGlow("MiaoJinBrush");
        e.Handled = true;
    }

    private void EndDragHint()
    {
        DropHint.Visibility = Visibility.Collapsed;
        VortexSpin.BeginAnimation(RotateTransform.AngleProperty, null);
        _hovering = false;
        _dropping = false;
        UpdateTargets();
    }

    // ═══════════════════════ 乾坤袋漩渦 ═══════════════════════

    private bool _dropping;

    /// <summary>袋口張開：漩渦從無到有彈開，並持續向內旋轉。</summary>
    private void OpenVortex()
    {
        VortexArms.Data ??= VortexGeometry(Vortex.Width / 2, arms: 3);

        var pop = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 },
        };
        VortexScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        VortexScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);

        // 逆時針旋轉：手臂由外往內捲，看起來像在把東西吸進中心
        VortexSpin.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, -360, TimeSpan.FromSeconds(2.4)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    /// <summary>吸入：漩渦加速旋轉並縮進中心，文字同時淡去。</summary>
    private void SwallowVortex(Action completed)
    {
        var duration = TimeSpan.FromMilliseconds(360);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var shrink = new DoubleAnimation(0, duration) { EasingFunction = ease };
        shrink.Completed += (_, _) => completed();
        VortexScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        VortexScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0, duration) { EasingFunction = ease });

        double angle = VortexSpin.Angle;
        VortexSpin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(angle, angle - 300, duration) { EasingFunction = ease });
        DropHintContent.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.2, duration) { FillBehavior = FillBehavior.Stop });
    }

    /// <summary>漩渦的手臂：數條由外緣捲向中心的螺旋線。</summary>
    private static Geometry VortexGeometry(double radius, int arms)
    {
        const int steps = 48;
        const double turns = 0.85; // 每條手臂繞多少圈
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            for (int a = 0; a < arms; a++)
            {
                double phase = a * 2 * Math.PI / arms;
                for (int i = 0; i <= steps; i++)
                {
                    double t = (double)i / steps;                    // 0 = 外緣、1 = 中心
                    double r = radius * Math.Pow(1 - t, 0.85) * 0.94 + 2;
                    double theta = phase + t * turns * 2 * Math.PI;
                    var p = new Point(radius + r * Math.Cos(theta), radius + r * Math.Sin(theta));
                    if (i == 0) ctx.BeginFigure(p, isFilled: false, isClosed: false);
                    else ctx.LineTo(p, isStroked: true, isSmoothJoin: true);
                }
            }
        }
        g.Freeze();
        return g;
    }

    // ═══════════════════════ 狀態燈 ═══════════════════════

    public void SetIndicator(string key, IslandIndicator? indicator) => Dispatcher.InvokeAsync(() =>
    {
        if (indicator is null) _indicators.Remove(key);
        else _indicators[key] = indicator;
        _indicatorsExpanded = null; // 強制重畫
        UpdateIndicators();
    });

    private bool? _indicatorsExpanded;

    /// <summary>
    /// 狀態燈：收合時是膠囊下緣正中的小光點（像鏡頭旁的指示燈，不會壓到時鐘或歌詞）；
    /// 展開時改成右上角的小圖示，看得出是麥克風還是相機。
    /// </summary>
    private void UpdateIndicators()
    {
        bool expanded = IsExpanded;
        if (_indicatorsExpanded == expanded) return;
        _indicatorsExpanded = expanded;

        IndicatorPanel.Children.Clear();
        IndicatorPanel.HorizontalAlignment = expanded ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        IndicatorPanel.VerticalAlignment = expanded ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        IndicatorPanel.Margin = expanded ? new Thickness(0, 7, 12, 0) : new Thickness(0, 0, 0, 3);

        foreach (var item in _indicators.Values)
        {
            var brush = TryFindResource(item.ToneKey) as Brush ?? (Brush)FindResource("ZhuShaBrush");
            if (expanded)
            {
                IndicatorPanel.Children.Add(new TextBlock
                {
                    Text = item.Glyph,
                    FontFamily = (FontFamily)FindResource("IconFont"),
                    FontSize = 10,
                    Margin = new Thickness(4, 0, 0, 0),
                    Foreground = brush,
                    ToolTip = item.Label,
                });
            }
            else
            {
                var color = brush is SolidColorBrush solid ? solid.Color : Colors.OrangeRed;
                IndicatorPanel.Children.Add(new System.Windows.Shapes.Ellipse
                {
                    Width = 4,
                    Height = 4,
                    Margin = new Thickness(2, 0, 2, 0),
                    Fill = brush,
                    ToolTip = item.Label,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 6, ShadowDepth = 0, Color = color, Opacity = 0.9,
                    },
                });
            }
        }
    }
}
