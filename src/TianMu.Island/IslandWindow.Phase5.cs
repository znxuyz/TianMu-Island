using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        var candidate = !IsExpanded && !_dragging && _ambient.Count >= 2 ? _ambient[^2] : null;
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
        if (e.Effects != DragDropEffects.None)
        {
            DropHintText.Text = _dropHandlers[^1].Hint;
            DropHint.Visibility = Visibility.Visible;
            _hovering = true;
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
        EndDragHint();
        if (!CanAcceptFiles(e) || e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        _dropHandlers[^1].OnDrop(files);
        e.Handled = true;
    }

    private void EndDragHint()
    {
        DropHint.Visibility = Visibility.Collapsed;
        _hovering = false;
        UpdateTargets();
    }

    // ═══════════════════════ 狀態燈 ═══════════════════════

    public void SetIndicator(string key, IslandIndicator? indicator) => Dispatcher.InvokeAsync(() =>
    {
        if (indicator is null) _indicators.Remove(key);
        else _indicators[key] = indicator;

        IndicatorPanel.Children.Clear();
        foreach (var item in _indicators.Values)
        {
            IndicatorPanel.Children.Add(new TextBlock
            {
                Text = item.Glyph,
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 9,
                Margin = new Thickness(3, 0, 0, 0),
                Foreground = TryFindResource(item.ToneKey) as Brush ?? (Brush)FindResource("ZhuShaBrush"),
                ToolTip = item.Label,
            });
        }
    });
}
