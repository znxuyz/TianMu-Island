using System.Windows;

namespace TianMu.Island.Shell;

/// <summary>天幕停靠的位置。</summary>
public enum DockPosition
{
    /// <summary>上方正中（預設）：貼齊螢幕上緣，像瀏海一樣。</summary>
    TopCenter,
    /// <summary>左上角：貼齊上緣。</summary>
    TopLeft,
    /// <summary>右上角：貼齊上緣。</summary>
    TopRight,
    /// <summary>左側中間：浮在左緣旁。</summary>
    Left,
    /// <summary>右側中間：浮在右緣旁。</summary>
    Right,
}

/// <summary>天幕貼住的螢幕邊緣。</summary>
[Flags]
public enum DockEdges
{
    None = 0,
    Top = 1,
    Left = 2,
    Right = 4,
}

/// <summary>
/// 停靠位置的計算（純數學，方便測試）。所有座標單位一致即可（DIP 或像素皆可）。
/// </summary>
public static class DockLayout
{
    /// <summary>上方三個位置貼齊螢幕上緣。</summary>
    public static bool IsFlushTop(DockPosition dock) => Edges(dock).HasFlag(DockEdges.Top);

    /// <summary>
    /// 天幕貼住哪些邊：正中貼上緣；角落同時貼上緣與側邊（嵌在角落裡）；左右側只貼側邊。
    /// </summary>
    public static DockEdges Edges(DockPosition dock) => dock switch
    {
        DockPosition.TopLeft => DockEdges.Top | DockEdges.Left,
        DockPosition.TopRight => DockEdges.Top | DockEdges.Right,
        DockPosition.Left => DockEdges.Left,
        DockPosition.Right => DockEdges.Right,
        _ => DockEdges.Top,
    };

    /// <summary>停在左右側時改為直式（收合時是一條直立的膠囊）。</summary>
    public static bool IsVertical(DockPosition dock) => dock is DockPosition.Left or DockPosition.Right;

    /// <summary>
    /// 拖曳放開時，依游標落在螢幕的哪一區決定停靠位置：
    /// 上方 30% 分成左、中、右三格；其餘依左右半邊停到側邊。
    /// </summary>
    public static DockPosition Choose(Point cursor, Rect screen)
    {
        double x = (cursor.X - screen.Left) / Math.Max(1, screen.Width);
        double y = (cursor.Y - screen.Top) / Math.Max(1, screen.Height);

        if (y < 0.3)
        {
            if (x < 1.0 / 3) return DockPosition.TopLeft;
            if (x > 2.0 / 3) return DockPosition.TopRight;
            return DockPosition.TopCenter;
        }
        return x < 0.5 ? DockPosition.Left : DockPosition.Right;
    }

    /// <summary>天幕視窗左上角應放在哪裡。</summary>
    /// <param name="bounds">整個螢幕範圍（貼齊上緣用）。</param>
    /// <param name="workArea">扣掉工作列後的範圍（側邊停靠用）。</param>
    /// <param name="windowSize">天幕視窗大小。</param>
    public static Point WindowOrigin(DockPosition dock, Rect bounds, Rect workArea, Size windowSize) => dock switch
    {
        DockPosition.TopLeft => new Point(workArea.Left, bounds.Top),
        DockPosition.TopRight => new Point(workArea.Right - windowSize.Width, bounds.Top),
        DockPosition.Left => new Point(workArea.Left, workArea.Top + (workArea.Height - windowSize.Height) / 2),
        DockPosition.Right => new Point(workArea.Right - windowSize.Width, workArea.Top + (workArea.Height - windowSize.Height) / 2),
        _ => new Point(bounds.Left + (bounds.Width - windowSize.Width) / 2, bounds.Top),
    };

    /// <summary>膠囊在視窗內的對齊方式（決定展開時往哪個方向長大）。全部緊貼邊緣、不留空隙。</summary>
    public static (HorizontalAlignment H, VerticalAlignment V, Thickness Margin) CapsuleAlignment(DockPosition dock) => dock switch
    {
        DockPosition.TopLeft => (HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(0)),
        DockPosition.TopRight => (HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0)),
        DockPosition.Left => (HorizontalAlignment.Left, VerticalAlignment.Center, new Thickness(0)),
        DockPosition.Right => (HorizontalAlignment.Right, VerticalAlignment.Center, new Thickness(0)),
        _ => (HorizontalAlignment.Center, VerticalAlignment.Top, new Thickness(0)),
    };
}
