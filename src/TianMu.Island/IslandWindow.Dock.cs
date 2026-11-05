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
///   · 正中：貼齊螢幕上緣，兩肩以曲面向兩側延伸，像瀏海一樣長在螢幕上
///   · 左上、右上：嵌在螢幕角落，同時貼住上緣與側邊，兩道曲面分別沿上緣與側邊延伸
///   · 左側、右側：直式，貼住側邊，曲面沿側邊上下延伸
///   · 按住膠囊拖動即可移動，放開後吸附到最近的位置，並記住是哪一個螢幕
/// </summary>
public partial class IslandWindow
{
    private const double ShoulderRadius = 40;   // 兩側曲面向外延伸的寬度
    private const double DragThresholdPx = 6;

    private DockPosition _dock = DockPosition.TopCenter;
    private string? _activeScreenName;
    private bool _dragging;
    private NativeMethods.POINT _dragStartCursor;
    private Point _dragStartWindow;

    /// <summary>目前貼住的邊（拖曳中是浮動的圓膠囊，不貼任何邊）。</summary>
    private DockEdges CurrentEdges => _dragging ? DockEdges.None : DockLayout.Edges(_dock);

    /// <summary>停在左右側時為直式（拖曳中恢復橫式）。</summary>
    private bool IsVertical => !_dragging && DockLayout.IsVertical(_dock);

    /// <summary>嵌在螢幕角落（同時貼住上緣與側邊）。</summary>
    private bool IsCorner => !_dragging && DockLayout.IsCorner(_dock);

    // ═══════════════════════ 停靠 ═══════════════════════

    /// <summary>依設定把天幕放到對應螢幕的停靠位置。</summary>
    private void ApplyDock(bool animate = false)
    {
        var (dock, screenName) = _settings.Read(s => (s.Dock, s.DockScreen));
        _dock = dock;

        // 多螢幕跟隨時，以滑鼠所在的螢幕為準；否則用使用者拖曳時選定的螢幕
        string? wanted = _followScreenName ?? screenName;
        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == wanted) ?? Forms.Screen.PrimaryScreen;
        if (screen is null) return;
        _activeScreenName = screen.DeviceName;

        // 透明畫布大小：橫式放得下最寬的展開面板；直式（左右側）要放得下直立的長膠囊
        (Width, Height) = DockLayout.IsVertical(dock) ? (540.0, 420.0) : (640.0, 260.0);

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
        Refresh();          // 直式／橫式可能改變，重新決定收合內容與尺寸
        ApplyVisuals();
    }

    /// <summary>
    /// 收合內容依方向切換：橫式顯示呼吸點＋時鐘；直式顯示上下排列的呼吸點與時鐘。
    /// 法器的收合畫面（例如音樂）在直式時轉 90 度，像側邊的書籤一樣直讀。
    /// </summary>
    private void UpdateCompactOrientation(bool hasModuleView)
    {
        bool vertical = IsVertical;
        DefaultCompact.Visibility = !hasModuleView && !vertical ? Visibility.Visible : Visibility.Collapsed;
        VerticalCompact.Visibility = !hasModuleView && vertical ? Visibility.Visible : Visibility.Collapsed;
        CompactHost.LayoutTransform = vertical
            ? new RotateTransform(_dock == DockPosition.Right ? 90 : -90)
            : Transform.Identity;
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
        var edges = CurrentEdges;
        // 角落形體：朝向畫面內側的那個角用大圓弧，像一片從螢幕角落長出來的扇形
        double inner = IsCorner ? IslandShape.CornerInnerRadius(w, h) : radius;
        CapsuleShape.Data = IslandShape.Build(w, h, radius, ShoulderRadius, edges, inner);
        ContentRoot.Clip = IslandShape.Build(w, h, radius, flare: 0, edges, inner);
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
        _followScreenName = null; // 使用者親手放的位置優先
        ApplyDock(animate: true);
        return true;
    }
}
