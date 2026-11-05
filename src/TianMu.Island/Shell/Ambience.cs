using System.Windows;
using System.Windows.Media;

namespace TianMu.Island.Shell;

/// <summary>
/// 全螢幕判斷：前景視窗是否蓋滿天幕所在的整個螢幕（看影片、玩遊戲、簡報時）。
/// </summary>
public static class FullscreenRule
{
    /// <summary>容許的誤差（像素）：有些全螢幕程式會多出或少掉一兩個像素的邊框。</summary>
    private const double Tolerance = 2;

    public static bool Covers(Rect window, Rect monitor) =>
        window.Left <= monitor.Left + Tolerance &&
        window.Top <= monitor.Top + Tolerance &&
        window.Right >= monitor.Right - Tolerance &&
        window.Bottom >= monitor.Bottom - Tolerance;

    /// <summary>桌面與工作列本身也是「蓋滿螢幕」的視窗，但不算全螢幕程式。</summary>
    public static bool IsShellWindow(string className) =>
        className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
}

/// <summary>
/// 磁吸：滑鼠靠近（但還沒碰到）天幕時，天幕朝游標方向微微偏移，像被吸過去。
/// </summary>
public static class Magnet
{
    /// <summary>
    /// 計算偏移量。游標距離膠囊邊緣越近吸力越強，碰到之後就不再偏移（交給懸停動畫）。
    /// </summary>
    /// <param name="cursor">游標位置。</param>
    /// <param name="capsule">膠囊範圍（與游標同一座標系）。</param>
    /// <param name="radius">開始感應的距離。</param>
    /// <param name="maxShift">最大偏移量。</param>
    public static Vector Offset(Point cursor, Rect capsule, double radius, double maxShift)
    {
        if (capsule.IsEmpty || capsule.Contains(cursor)) return default;

        // 游標到膠囊邊緣最近點的向量
        double nearestX = Math.Clamp(cursor.X, capsule.Left, capsule.Right);
        double nearestY = Math.Clamp(cursor.Y, capsule.Top, capsule.Bottom);
        var toCursor = new Vector(cursor.X - nearestX, cursor.Y - nearestY);
        double distance = toCursor.Length;
        if (distance >= radius || distance <= 0) return default;

        double strength = 1 - distance / radius;            // 0（遠）→ 1（貼近）
        strength = strength * strength * (3 - 2 * strength); // 平滑曲線，邊緣不會突然抽動
        toCursor.Normalize();
        return toCursor * (maxShift * strength);
    }
}

/// <summary>
/// 晝夜變色：玄漆的色溫隨時辰極輕微地流轉。
///   子夜偏墨藍、卯時清晨回到本色、午時偏暖、酉時黃昏帶一點赭紅。
/// 變化刻意壓得很低，只有長時間看才感覺得到。
/// </summary>
public static class DayNight
{
    private static readonly (double Hour, Color Top, Color Body)[] Anchors =
    [
        (0, Color.FromRgb(0x14, 0x16, 0x1B), Color.FromRgb(0x0B, 0x0C, 0x0F)),   // 子：墨藍
        (6, Color.FromRgb(0x1C, 0x19, 0x15), Color.FromRgb(0x0E, 0x0C, 0x0A)),   // 卯：本色
        (12, Color.FromRgb(0x21, 0x1B, 0x13), Color.FromRgb(0x10, 0x0D, 0x09)),  // 午：暖
        (18, Color.FromRgb(0x1F, 0x17, 0x14), Color.FromRgb(0x0F, 0x0B, 0x0A)),  // 酉：赭
        (24, Color.FromRgb(0x14, 0x16, 0x1B), Color.FromRgb(0x0B, 0x0C, 0x0F)),  // 回到子
    ];

    /// <summary>此刻漆面的受光色與本體色。</summary>
    public static (Color Top, Color Body) ColorsAt(TimeSpan timeOfDay)
    {
        double h = timeOfDay.TotalHours % 24;
        for (int i = 0; i < Anchors.Length - 1; i++)
        {
            var (h0, top0, body0) = Anchors[i];
            var (h1, top1, body1) = Anchors[i + 1];
            if (h < h0 || h > h1) continue;

            double t = (h - h0) / (h1 - h0);
            t = t * t * (3 - 2 * t);
            return (Lerp(top0, top1, t), Lerp(body0, body1, t));
        }
        return (Anchors[0].Top, Anchors[0].Body);
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));
}
