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

/// <summary>
/// 停靠位置的計算（純數學，方便測試）。所有座標單位一致即可（DIP 或像素皆可）。
/// </summary>
public static class DockLayout
{
    /// <summary>上方三個位置貼齊螢幕上緣，左右兩側則浮在邊緣旁。</summary>
    public static bool IsFlushTop(DockPosition dock) =>
        dock is DockPosition.TopCenter or DockPosition.TopLeft or DockPosition.TopRight;

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
        DockPosition.TopLeft => new Point(bounds.Left, bounds.Top),
        DockPosition.TopRight => new Point(bounds.Right - windowSize.Width, bounds.Top),
        DockPosition.Left => new Point(workArea.Left, workArea.Top + (workArea.Height - windowSize.Height) / 2),
        DockPosition.Right => new Point(workArea.Right - windowSize.Width, workArea.Top + (workArea.Height - windowSize.Height) / 2),
        _ => new Point(bounds.Left + (bounds.Width - windowSize.Width) / 2, bounds.Top),
    };

    /// <summary>膠囊在視窗內的對齊方式（決定展開時往哪個方向長大）。</summary>
    public static (HorizontalAlignment H, VerticalAlignment V, Thickness Margin) CapsuleAlignment(DockPosition dock) => dock switch
    {
        DockPosition.TopLeft => (HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(44, 0, 0, 0)),
        DockPosition.TopRight => (HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, 0, 44, 0)),
        DockPosition.Left => (HorizontalAlignment.Left, VerticalAlignment.Center, new Thickness(10, 0, 0, 0)),
        DockPosition.Right => (HorizontalAlignment.Right, VerticalAlignment.Center, new Thickness(0, 0, 10, 0)),
        _ => (HorizontalAlignment.Center, VerticalAlignment.Top, new Thickness(0)),
    };
}
