using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TianMu.Island.Interop;
using TianMu.Island.Shell;
using Forms = System.Windows.Forms;

namespace TianMu.Island;

/// <summary>
/// 天幕本體的第三部分：停靠位置、貼邊形體、拖曳移動。
///
///   · 上方三個位置（左上、正中、右上）：貼齊螢幕上緣，上方是平的，兩肩向外彎出，像瀏海一樣長在螢幕上
///   · 左側、右側：浮在邊緣旁的圓膠囊
///   · 按住膠囊拖動即可移動，放開後吸附到最近的位置，並記住是哪一個螢幕
/// </summary>
public partial class IslandWindow
{
    private const double ShoulderRadius = 10;
    private const double DragThresholdPx = 6;

    private DockPosition _dock = DockPosition.TopCenter;
    private bool _dragging;
    private NativeMethods.POINT _dragStartCursor;
    private Point _dragStartWindow;

    private bool FlushTop => !_dragging && DockLayout.IsFlushTop(_dock);

    // ═══════════════════════ 停靠 ═══════════════════════

    /// <summary>依設定把天幕放到對應螢幕的停靠位置。</summary>
    private void ApplyDock(bool animate = false)
    {
        var (dock, screenName) = _settings.Read(s => (s.Dock, s.DockScreen));
        _dock = dock;

        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == screenName) ?? Forms.Screen.PrimaryScreen;
        if (screen is null) return;

        var (bounds, workArea) = (ToDip(screen.Bounds), ToDip(screen.WorkingArea));
        var origin = DockLayout.WindowOrigin(dock, bounds, workArea, new Size(Width, Height));

        var (h, v, margin) = DockLayout.CapsuleAlignment(dock);
        foreach (var element in new FrameworkElement[] { Capsule, EdgeRun })
        {
            element.HorizontalAlignment = h;
            element.VerticalAlignment = v;
            element.Margin = margin;
        }

        MoveWindow(origin, animate);
        ApplyVisuals();
    }

    private void MoveWindow(Point origin, bool animate)
    {
        if (!animate || !SystemParameters.ClientAreaAnimation)
        {
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Left = origin.X;
            Top = origin.Y;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(280);
        var toX = new DoubleAnimation(origin.X, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        var toY = new DoubleAnimation(origin.Y, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        toX.Completed += (_, _) => Left = origin.X;
        toY.Completed += (_, _) => Top = origin.Y;
        BeginAnimation(LeftProperty, toX);
        BeginAnimation(TopProperty, toY);
    }

    /// <summary>螢幕座標（實際像素）轉成本視窗的 DIP。</summary>
    private Rect ToDip(System.Drawing.Rectangle r)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Rect(r.Left / dpi.DpiScaleX, r.Top / dpi.DpiScaleY, r.Width / dpi.DpiScaleX, r.Height / dpi.DpiScaleY);
    }

    // ═══════════════════════ 形體 ═══════════════════════

    /// <summary>依目前尺寸畫出天幕的外形，並讓內容沿著外形裁切。</summary>
    private void ApplyShape(double w, double h, double radius)
    {
        Capsule.CornerRadius = new CornerRadius(0);
        bool flush = FlushTop;
        CapsuleShape.Data = BuildShape(w, h, radius, flush ? ShoulderRadius : 0, flush);
        ContentRoot.Clip = BuildShape(w, h, radius, shoulder: 0, flushTop: flush);
    }

    /// <summary>
    /// 天幕外形。貼齊上緣時：上方兩角是直角，兩側各有一道向外彎的「肩」接到螢幕邊緣；
    /// 否則是一般的圓角膠囊。
    /// </summary>
    internal static Geometry BuildShape(double w, double h, double r, double shoulder, bool flushTop)
    {
        if (!flushTop)
        {
            var pill = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
            pill.Freeze();
            return pill;
        }

        double s = Math.Min(shoulder, Math.Max(0, h - r));
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(-s, 0), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(w + s, 0), true, false);
            if (s > 0)
                ctx.ArcTo(new Point(w, s), new Size(s, s), 0, false, SweepDirection.Counterclockwise, true, false);
            ctx.LineTo(new Point(w, h - r), true, false);
            ctx.ArcTo(new Point(w - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(r, h), true, false);
            ctx.ArcTo(new Point(0, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(0, s), true, false);
            if (s > 0)
                ctx.ArcTo(new Point(-s, 0), new Size(s, s), 0, false, SweepDirection.Counterclockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    // ═══════════════════════ 拖曳 ═══════════════════════

    private void BeginPress()
    {
        NativeMethods.GetCursorPos(out _dragStartCursor);
        _dragStartWindow = new Point(Left, Top);
        _dragging = false;
    }

    private void Capsule_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_pressed || e.LeftButton != MouseButtonState.Pressed) return;

        NativeMethods.GetCursorPos(out var now);
        double dx = now.X - _dragStartCursor.X, dy = now.Y - _dragStartCursor.Y;

        if (!_dragging)
        {
            if (Math.Abs(dx) < DragThresholdPx && Math.Abs(dy) < DragThresholdPx) return;

            // 開始拖曳：收起天幕，變成浮動膠囊跟著游標走
            _dragging = true;
            _userExpanded = false;
            _transient = null;
            _transientTimer.Stop();
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Refresh();
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        Left = _dragStartWindow.X + dx / dpi.DpiScaleX;
        Top = _dragStartWindow.Y + dy / dpi.DpiScaleY;
    }

    /// <summary>放開時：若剛才在拖曳，吸附到最近的停靠位置並記住。回傳是否處理了拖曳。</summary>
    private bool EndPressAsDrag()
    {
        if (!_dragging) return false;
        _dragging = false;

        NativeMethods.GetCursorPos(out var cursor);
        var point = new System.Drawing.Point(cursor.X, cursor.Y);
        var screen = Forms.Screen.FromPoint(point);
        var b = screen.Bounds;
        var dock = DockLayout.Choose(new Point(cursor.X, cursor.Y), new Rect(b.Left, b.Top, b.Width, b.Height));

        _settings.Update(s =>
        {
            s.Dock = dock;
            s.DockScreen = screen.DeviceName;
        });
        ApplyDock(animate: true);
        return true;
    }
}
