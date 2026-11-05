using System.Windows;
using System.Windows.Media;

namespace TianMu.Island.Shell;

/// <summary>天幕的外形（純幾何，方便測試）。</summary>
public static class IslandShape
{
    /// <summary>角落形體朝內那個角的圓弧半徑。</summary>
    public static double CornerInnerRadius(double w, double h)
    {
        double s = Math.Min(w, h);
        // 收合時圓弧幾乎佔滿厚度，像一片扇；展開後收斂一些，免得裁到面板內容
        return s <= 60 ? s * 0.7 : Math.Min(s * 0.45, 56);
    }

    /// <summary>
    /// 天幕外形。四個角依「貼住哪些邊」決定：
    ///   · 兩邊都貼住（嵌在螢幕角落）：直角，剛好填滿角落
    ///   · 只貼住其中一邊：沿著那一邊以平滑曲面向外延伸、融進螢幕邊緣
    ///   · 都沒貼：一般圓角
    /// 整條輪廓的切線連續，沒有折角。
    /// </summary>
    /// <param name="flare">曲面沿邊緣延伸的長度（0 = 不延伸，用於裁切內容）。</param>
    /// <param name="innerRadius">嵌在角落時，朝向畫面內側那個角的圓弧半徑（預設與 <paramref name="r"/> 相同）。</param>
    public static Geometry Build(double w, double h, double r, double flare, DockEdges edges, double innerRadius = -1)
    {
        if (edges == DockEdges.None)
        {
            var pill = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
            pill.Freeze();
            return pill;
        }

        r = Math.Max(0, Math.Min(r, Math.Min(w, h) / 2));
        const double k = 0.6; // 控制點位置，越大曲面越柔、延伸得越開
        bool top = edges.HasFlag(DockEdges.Top), left = edges.HasFlag(DockEdges.Left), right = edges.HasFlag(DockEdges.Right);
        bool corner = top && (left || right);
        double ri = innerRadius < 0 || !corner ? r : Math.Clamp(innerRadius, 0, Math.Max(0, Math.Min(w, h) - 8));
        double rMax = Math.Max(r, ri); // 曲面延伸時要讓出相鄰圓角的空間

        // 依順時針：左上、右上、右下、左下。a = 進入此角的行進方向、b = 離開此角的方向。
        var corners = new (Point P, Vector A, Vector B, bool InAttached, bool OutAttached, double InSide, double OutSide)[]
        {
            (new Point(0, 0), new Vector(0, -1), new Vector(1, 0), left, top, h, w),
            (new Point(w, 0), new Vector(1, 0), new Vector(0, 1), top, right, w, h),
            (new Point(w, h), new Vector(0, 1), new Vector(-1, 0), right, false, h, w),
            (new Point(0, h), new Vector(-1, 0), new Vector(0, -1), false, left, w, h),
        };

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            bool started = false;
            foreach (var (p, a, b, inAttached, outAttached, inSide, outSide) in corners)
            {
                Point entry;
                Action draw;

                if (inAttached && outAttached)
                {
                    entry = p;                                   // 嵌在角落：直角
                    draw = () => { };
                }
                else if (inAttached)
                {
                    // 來的那一邊貼住螢幕：先沿邊緣多延伸一段，再以曲面彎進離開的那一邊
                    double d = Math.Min(flare * 0.8, Math.Max(0, outSide - rMax));
                    entry = p + a * flare;
                    draw = () => ctx.BezierTo(p + a * (flare * k), p + b * (d * k), p + b * d, true, true);
                }
                else if (outAttached)
                {
                    // 去的那一邊貼住螢幕：從自由邊以曲面彎出，沿邊緣延伸出去
                    double d = Math.Min(flare * 0.8, Math.Max(0, inSide - rMax));
                    entry = p - a * d;
                    draw = () => ctx.BezierTo(p - a * (d * k), p - b * (flare * k), p - b * flare, true, true);
                }
                else
                {
                    double rc = corner ? ri : r;                 // 一般圓角（角落形體朝內的角用大圓弧）
                    entry = p - a * rc;
                    draw = () => ctx.ArcTo(p + b * rc, new Size(rc, rc), 0, false, SweepDirection.Clockwise, true, true);
                }

                if (!started)
                {
                    ctx.BeginFigure(entry, isFilled: true, isClosed: true);
                    started = true;
                }
                else
                {
                    ctx.LineTo(entry, true, false);
                }
                draw();
            }
        }
        geometry.Freeze();
        return geometry;
    }
}
