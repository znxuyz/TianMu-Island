using System.Windows;

namespace TianMu.Island.Shell;

/// <summary>連接兩顆圓的「水滴頸」：四個接點與四個控制點，畫成兩段貝茲曲線。</summary>
public readonly record struct MetaballNeck(Point P1, Point H1, Point H3, Point P3, Point P4, Point H4, Point H2, Point P2);

/// <summary>
/// 水滴融合：兩顆圓靠近時，中間長出一段平滑的「頸」把它們連在一起；拉開時頸越來越細，最後斷開。
/// 這是經典的 metaball 連接演算法（以兩圓的切線方向與距離決定接點與控制點）。純數學，方便測試。
/// </summary>
public static class Metaball
{
    private const double HandleSize = 2.4;

    /// <param name="c1">大圓（天幕端點）圓心。</param>
    /// <param name="r1">大圓半徑。</param>
    /// <param name="c2">小圓（分身）圓心。</param>
    /// <param name="r2">小圓半徑。</param>
    /// <param name="spread">頸的寬窄（0~1，越大越肥厚）。</param>
    /// <returns>兩圓太遠（已斷開）或完全重疊時回傳 null。</returns>
    public static MetaballNeck? Connect(Point c1, double r1, Point c2, double r2, double spread = 0.5)
    {
        double d = (c2 - c1).Length;
        double maxDistance = r1 + r2 * 2.5;
        if (r1 <= 0 || r2 <= 0 || d > maxDistance || d <= Math.Abs(r1 - r2)) return null;

        double u1 = 0, u2 = 0;
        if (d < r1 + r2)
        {
            u1 = Math.Acos(Math.Clamp((r1 * r1 + d * d - r2 * r2) / (2 * r1 * d), -1, 1));
            u2 = Math.Acos(Math.Clamp((r2 * r2 + d * d - r1 * r1) / (2 * r2 * d), -1, 1));
        }

        double between = Math.Atan2(c2.Y - c1.Y, c2.X - c1.X);
        double maxSpread = Math.Acos(Math.Clamp((r1 - r2) / d, -1, 1));

        double a1 = between + u1 + (maxSpread - u1) * spread;
        double a2 = between - u1 - (maxSpread - u1) * spread;
        double a3 = between + Math.PI - u2 - (Math.PI - u2 - maxSpread) * spread;
        double a4 = between - Math.PI + u2 + (Math.PI - u2 - maxSpread) * spread;

        Point p1 = Along(c1, a1, r1), p2 = Along(c1, a2, r1);
        Point p3 = Along(c2, a3, r2), p4 = Along(c2, a4, r2);

        double total = r1 + r2;
        double handle = Math.Min(spread * HandleSize, (p3 - p1).Length / total) * Math.Min(1, d * 2 / total);
        double h1Len = r1 * handle, h2Len = r2 * handle;

        return new MetaballNeck(
            p1, Along(p1, a1 - Math.PI / 2, h1Len),
            Along(p3, a3 + Math.PI / 2, h2Len), p3,
            p4, Along(p4, a4 - Math.PI / 2, h2Len),
            Along(p2, a2 + Math.PI / 2, h1Len), p2);
    }

    private static Point Along(Point origin, double angle, double length) =>
        new(origin.X + length * Math.Cos(angle), origin.Y + length * Math.Sin(angle));
}
