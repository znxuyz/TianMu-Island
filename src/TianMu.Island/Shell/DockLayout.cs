using System.Windows;

namespace TianMu.Island.Shell;

/// <summary>
/// 天幕停靠的位置。上、下、左、右四條邊可以停在任何一點（位置另存於 <c>DockOffset</c>）；
/// 四個角落則嵌在角落裡，收合時是正方形。
/// </summary>
public enum DockPosition
{
    /// <summary>上緣（預設正中）：貼齊螢幕上緣，像瀏海一樣。名稱沿用舊版，現在可停在上緣任何一點。</summary>
    TopCenter,
    /// <summary>左上角。</summary>
    TopLeft,
    /// <summary>右上角。</summary>
    TopRight,
    /// <summary>左緣：直式，可停在左緣任何高度。</summary>
    Left,
    /// <summary>右緣：直式，可停在右緣任何高度。</summary>
    Right,
    /// <summary>下緣：貼在工作列上方，可停在任何一點；展開時往上長。</summary>
    Bottom,
    /// <summary>左下角。</summary>
    BottomLeft,
    /// <summary>右下角。</summary>
    BottomRight,
}

/// <summary>天幕貼住的螢幕邊緣。</summary>
[Flags]
public enum DockEdges
{
    None = 0,
    Top = 1,
    Left = 2,
    Right = 4,
    Bottom = 8,
}

/// <summary>
/// 停靠位置的計算（純數學，方便測試）。所有座標單位一致即可（DIP 或像素皆可）。
/// </summary>
public static class DockLayout
{
    /// <summary>上方的位置貼齊螢幕上緣。</summary>
    public static bool IsFlushTop(DockPosition dock) => Edges(dock).HasFlag(DockEdges.Top);

    /// <summary>天幕貼住哪些邊：角落貼兩條、其餘貼一條。</summary>
    public static DockEdges Edges(DockPosition dock) => dock switch
    {
        DockPosition.TopLeft => DockEdges.Top | DockEdges.Left,
        DockPosition.TopRight => DockEdges.Top | DockEdges.Right,
        DockPosition.BottomLeft => DockEdges.Bottom | DockEdges.Left,
        DockPosition.BottomRight => DockEdges.Bottom | DockEdges.Right,
        DockPosition.Left => DockEdges.Left,
        DockPosition.Right => DockEdges.Right,
        DockPosition.Bottom => DockEdges.Bottom,
        _ => DockEdges.Top,
    };

    /// <summary>停在左右側時改為直式（收合時是一條直立的膠囊）。</summary>
    public static bool IsVertical(DockPosition dock) => dock is DockPosition.Left or DockPosition.Right;

    /// <summary>嵌在螢幕角落（同時貼住兩條邊）：收合時是正方形。</summary>
    public static bool IsCorner(DockPosition dock) => dock is DockPosition.TopLeft or DockPosition.TopRight or DockPosition.BottomLeft or DockPosition.BottomRight;

    /// <summary>貼在下緣（展開時往上長）。</summary>
    public static bool IsBottom(DockPosition dock) => Edges(dock).HasFlag(DockEdges.Bottom);

    /// <summary>停在畫面右半邊的角落（分身小圓要往左長）。</summary>
    public static bool IsRightCorner(DockPosition dock) => dock is DockPosition.TopRight or DockPosition.BottomRight;

    /// <summary>角落的判定範圍：游標離兩條邊都在這個距離內，就嵌進角落。</summary>
    public static double CornerZone(Rect screen) => Math.Clamp(Math.Min(screen.Width, screen.Height) * 0.12, 110, 200);

    /// <summary>
    /// 拖曳放開時決定停在哪裡：靠近角落就嵌進角落；否則貼到最近的那條邊，
    /// 並記下沿著那條邊的位置（0 = 左／上端，1 = 右／下端）。
    /// </summary>
    public static (DockPosition Dock, double Offset) Place(Point cursor, Rect screen)
    {
        double dl = cursor.X - screen.Left, dr = screen.Right - cursor.X;
        double dt = cursor.Y - screen.Top, db = screen.Bottom - cursor.Y;
        double zone = CornerZone(screen);

        bool nearLeft = dl <= zone, nearRight = dr <= zone, nearTop = dt <= zone, nearBottom = db <= zone;
        if (nearTop && nearLeft) return (DockPosition.TopLeft, 0);
        if (nearTop && nearRight) return (DockPosition.TopRight, 1);
        if (nearBottom && nearLeft) return (DockPosition.BottomLeft, 0);
        if (nearBottom && nearRight) return (DockPosition.BottomRight, 1);

        double along = Math.Clamp((cursor.X - screen.Left) / Math.Max(1, screen.Width), 0, 1);
        double down = Math.Clamp((cursor.Y - screen.Top) / Math.Max(1, screen.Height), 0, 1);
        double min = Math.Min(Math.Min(dl, dr), Math.Min(dt, db));
        if (min == dt) return (DockPosition.TopCenter, along);
        if (min == db) return (DockPosition.Bottom, along);
        return (dl <= dr ? DockPosition.Left : DockPosition.Right, down);
    }

    /// <summary>只要停靠位置、不管沿邊位置時用（舊介面）。</summary>
    public static DockPosition Choose(Point cursor, Rect screen) => Place(cursor, screen).Dock;

    /// <summary>天幕視窗左上角應放在哪裡（沿邊位置預設正中）。</summary>
    public static Point WindowOrigin(DockPosition dock, Rect bounds, Rect workArea, Size windowSize) =>
        WindowOrigin(dock, 0.5, bounds, workArea, windowSize);

    /// <summary>
    /// 天幕視窗左上角應放在哪裡。膠囊在視窗正中，所以靠近螢幕兩端時視窗會被擋在螢幕內，
    /// 確保展開的面板不會跑出螢幕。
    /// </summary>
    /// <param name="offset">沿著那條邊的位置（0～1）。</param>
    /// <param name="bounds">整個螢幕範圍（貼齊上緣用）。</param>
    /// <param name="workArea">扣掉工作列後的範圍（側邊、下緣停靠用）。</param>
    /// <param name="windowSize">天幕視窗大小。</param>
    public static Point WindowOrigin(DockPosition dock, double offset, Rect bounds, Rect workArea, Size windowSize)
    {
        offset = double.IsFinite(offset) ? Math.Clamp(offset, 0, 1) : 0.5;
        double w = windowSize.Width, h = windowSize.Height;
        double AlongX(Rect area) => Clamp(bounds.Left + offset * bounds.Width - w / 2, area.Left, area.Right - w);
        double AlongY() => Clamp(bounds.Top + offset * bounds.Height - h / 2, workArea.Top, workArea.Bottom - h);
        return dock switch
        {
            DockPosition.TopLeft => new Point(workArea.Left, bounds.Top),
            DockPosition.TopRight => new Point(workArea.Right - w, bounds.Top),
            DockPosition.BottomLeft => new Point(workArea.Left, workArea.Bottom - h),
            DockPosition.BottomRight => new Point(workArea.Right - w, workArea.Bottom - h),
            DockPosition.Left => new Point(workArea.Left, AlongY()),
            DockPosition.Right => new Point(workArea.Right - w, AlongY()),
            DockPosition.Bottom => new Point(AlongX(workArea), workArea.Bottom - h),
            _ => new Point(AlongX(bounds), bounds.Top),
        };
    }

    /// <summary>螢幕比視窗還小時以起點為準，不丟例外。</summary>
    private static double Clamp(double v, double min, double max) => max < min ? min : Math.Clamp(v, min, max);

    /// <summary>膠囊在視窗內的對齊方式（決定展開時往哪個方向長大）。全部緊貼邊緣、不留空隙。</summary>
    public static (HorizontalAlignment H, VerticalAlignment V, Thickness Margin) CapsuleAlignment(DockPosition dock) => dock switch
    {
        DockPosition.TopLeft => (HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(0)),
        DockPosition.TopRight => (HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0)),
        DockPosition.BottomLeft => (HorizontalAlignment.Left, VerticalAlignment.Bottom, new Thickness(0)),
        DockPosition.BottomRight => (HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0)),
        DockPosition.Left => (HorizontalAlignment.Left, VerticalAlignment.Center, new Thickness(0)),
        DockPosition.Right => (HorizontalAlignment.Right, VerticalAlignment.Center, new Thickness(0)),
        DockPosition.Bottom => (HorizontalAlignment.Center, VerticalAlignment.Bottom, new Thickness(0)),
        _ => (HorizontalAlignment.Center, VerticalAlignment.Top, new Thickness(0)),
    };
}
