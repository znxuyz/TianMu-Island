using System.Windows;
using System.Windows.Media;

namespace TianMu.Island.Shell;

/// <summary>
/// 法陣盤的幾何（純數學，方便測試）：扇區從正上方開始順時針排列，中間是陣眼。
/// </summary>
public static class RadialGeometry
{
    public const double HubRadius = 52;     // 陣眼
    public const double OuterRadius = 150;  // 外緣
    public const double Gap = 0.035;        // 扇區間的縫（弧度）

    /// <summary>第 i 個扇區（共 n 個）的起訖角度，0 在右方、順時針為正（螢幕座標）。</summary>
    public static (double Start, double End, double Mid) SliceAngles(int i, int n)
    {
        double step = 2 * Math.PI / n;
        double start = -Math.PI / 2 - step / 2 + i * step + Gap;
        double end = -Math.PI / 2 - step / 2 + (i + 1) * step - Gap;
        return (start, end, (start + end) / 2);
    }

    /// <summary>
    /// 某一點落在哪個扇區：在陣眼裡回傳 -1（取消），超出外緣一段距離回傳 -2（盤外），否則回傳扇區索引。
    /// </summary>
    public static int HitTest(Point p, Point center, int n, double slack = 40)
    {
        if (n <= 0) return -2;
        double dx = p.X - center.X, dy = p.Y - center.Y;
        double r = Math.Sqrt(dx * dx + dy * dy);
        if (r < HubRadius) return -1;
        if (r > OuterRadius + slack) return -2;

        double step = 2 * Math.PI / n;
        // 角度轉成「從第 0 個扇區的起點量起」
        double a = Math.Atan2(dy, dx) + Math.PI / 2 + step / 2;
        a %= 2 * Math.PI;
        if (a < 0) a += 2 * Math.PI;
        return Math.Min(n - 1, (int)(a / step));
    }

    /// <summary>一個扇區（環形的一段）的外形。</summary>
    public static Geometry SlicePath(Point c, double a0, double a1, double r1, double r2)
    {
        Point P(double r, double a) => new(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
        bool large = a1 - a0 > Math.PI;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(P(r2, a0), isFilled: true, isClosed: true);
            ctx.ArcTo(P(r2, a1), new Size(r2, r2), 0, large, SweepDirection.Clockwise, true, true);
            ctx.LineTo(P(r1, a1), true, true);
            ctx.ArcTo(P(r1, a0), new Size(r1, r1), 0, large, SweepDirection.Counterclockwise, true, true);
        }
        g.Freeze();
        return g;
    }

    // ── 展開動畫「金線輪轉」：金線先畫圈，扇區再從陣眼往外、順時針接力長出 ──

    public static readonly TimeSpan OpenDuration = TimeSpan.FromMilliseconds(1300);

    /// <summary>外圈金線畫了多少（0~1）。</summary>
    public static double OutlineProgress(double ms) => CubicOut(Clamp01(ms / 520));

    /// <summary>第 i 個扇區的大小、不透明度與文字不透明度。</summary>
    public static (double Scale, double Opacity, double TextOpacity) Slice(double ms, int i)
    {
        double p = Clamp01((ms - 300 - i * 70) / 380);
        double scale = 0.25 + 0.75 * BackOut(p, 1.3);
        double text = Clamp01((ms - 520 - i * 70) / 260);
        return (scale, Clamp01(p * 2.4), text);
    }

    public static double HubOpacity(double ms) => Clamp01(ms / 260);

    private static double Clamp01(double t) => Math.Min(1, Math.Max(0, t));
    private static double CubicOut(double t) => 1 - Math.Pow(1 - t, 3);
    private static double BackOut(double t, double s) => 1 + (s + 1) * Math.Pow(t - 1, 3) + s * Math.Pow(t - 1, 2);
}
