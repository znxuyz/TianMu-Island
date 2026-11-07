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

    // ── 展開動畫「金線輪轉」：金線從第一格的左緣出發順時針畫一圈，掃到哪裡、扇區就長到哪裡 ──

    public const double SweepMs = 900;
    public static readonly TimeSpan OpenDuration = TimeSpan.FromMilliseconds(SweepMs + 400);

    /// <summary>金線起點（第一格的左緣）。</summary>
    public static double StartAngle(int n) => -Math.PI / 2 - Math.PI / Math.Max(1, n);

    /// <summary>金線畫了多少（0~1，等速，扇區才跟得上）。</summary>
    public static double OutlineProgress(double ms) => Clamp01(ms / SweepMs);

    /// <summary>金線筆尖目前的角度。</summary>
    public static double SweepAngle(double ms, int n) => StartAngle(n) + OutlineProgress(ms) * 2 * Math.PI;

    /// <summary>
    /// 第 i 格此刻的樣子：畫到哪個角度（金線還沒到時等於起點，代表不畫）、往外長了多少（0.55 到 1，帶一點回彈）、文字不透明度。
    /// </summary>
    public static (double Edge, double Grow, double TextOpacity) Slice(double ms, int i, int n)
    {
        var (a0, a1, mid) = SliceAngles(i, n);
        double sweep = SweepAngle(ms, n);
        if (sweep <= a0) return (a0, 0, 0);

        double edge = Math.Min(a1, sweep);
        double reachedAt = (a0 - StartAngle(n)) / (2 * Math.PI) * SweepMs;   // 金線到達這一格的時刻
        double grow = 0.55 + 0.45 * BackOut(Clamp01((ms - reachedAt) / 340), 1.4);
        double midAt = (mid - StartAngle(n)) / (2 * Math.PI) * SweepMs;
        double text = sweep >= mid ? Clamp01((ms - midAt) / 220) : 0;
        return (edge, grow, text);
    }

    public static double HubOpacity(double ms) => Clamp01(ms / 200);

    // ── 入爐：化形爐連同檔案縮進陣眼，其他扇區收起 ──

    public const double AbsorbMs = 450;
    public const double AbsorbPauseMs = 200;

    /// <summary>入爐的進度（0~1，先慢後快再慢）。</summary>
    public static double AbsorbProgress(double ms)
    {
        double t = Clamp01(ms / AbsorbMs);
        return t < .5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }

    /// <summary>化形爐扇區在入爐途中的縮放與位移（沿扇區中線往陣眼直線前進）。</summary>
    public static (double Scale, double Offset) FurnaceAbsorb(double p)
    {
        double rm = (HubRadius + OuterRadius) / 2;
        double scale = 1 - 0.75 * p;
        return (scale, rm * (1 - p) - scale * rm);
    }

    private static double Clamp01(double t) => Math.Min(1, Math.Max(0, t));
    private static double BackOut(double t, double s) => 1 + (s + 1) * Math.Pow(t - 1, 3) + s * Math.Pow(t - 1, 2);
}
