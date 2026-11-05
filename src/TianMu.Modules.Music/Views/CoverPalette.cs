using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TianMu.Modules.Music.Views;

/// <summary>
/// 封面取色：從專輯封面挑出最有代表性的顏色，再「收斂」成符合玄漆描金的色調
/// （降低彩度、提亮），讓頻譜與進度條隨每首歌換色，但不會花俏刺眼。
/// 封面幾乎是黑白灰時，回到描金。
/// </summary>
public static class CoverPalette
{
    public static readonly Color Gold = Color.FromRgb(0xC8, 0xA8, 0x69);

    /// <summary>從 BGRA32 像素挑色（純計算，方便測試）。</summary>
    public static Color Pick(ReadOnlySpan<byte> bgra)
    {
        double weightSum = 0, h = 0, s = 0, l = 0, hx = 0, hy = 0;

        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            if (bgra[i + 3] < 128) continue; // 透明像素
            var (ph, ps, pl) = ToHsl(bgra[i + 2], bgra[i + 1], bgra[i]);
            if (pl < 0.12 || pl > 0.92) continue; // 太暗或太亮的不具代表性

            // 越鮮豔、越接近中間亮度的像素權重越高
            double weight = ps * ps * (1 - Math.Abs(pl - 0.5) * 1.6);
            if (weight <= 0) continue;

            hx += Math.Cos(ph * Math.PI * 2) * weight;
            hy += Math.Sin(ph * Math.PI * 2) * weight;
            s += ps * weight;
            l += pl * weight;
            weightSum += weight;
        }

        if (weightSum < 0.5) return Gold; // 黑白灰封面

        h = Math.Atan2(hy, hx) / (Math.PI * 2);
        if (h < 0) h += 1;
        s /= weightSum;

        // 收斂：彩度 0.25~0.5、亮度固定在 0.66，與描金同一個「重量」
        double tamedS = Math.Clamp(s * 0.75, 0.25, 0.5);
        return FromHsl(h, tamedS, 0.66);
    }

    /// <summary>從封面圖片取色（先縮成 24×24 再計算，很快）。</summary>
    public static Color Pick(BitmapSource? cover)
    {
        if (cover is null) return Gold;
        try
        {
            var small = new TransformedBitmap(cover, new ScaleTransform(24.0 / cover.PixelWidth, 24.0 / cover.PixelHeight));
            var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            int stride = bgra.PixelWidth * 4;
            var pixels = new byte[stride * bgra.PixelHeight];
            bgra.CopyPixels(pixels, stride, 0);
            return Pick(pixels);
        }
        catch
        {
            return Gold;
        }
    }

    public static (double H, double S, double L) ToHsl(byte r8, byte g8, byte b8)
    {
        double r = r8 / 255.0, g = g8 / 255.0, b = b8 / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2;
        if (max - min < 1e-9) return (0, 0, l);

        double d = max - min;
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0)
                 : max == g ? (b - r) / d + 2
                 : (r - g) / d + 4;
        return (h / 6, s, l);
    }

    public static Color FromHsl(double h, double s, double l)
    {
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        byte Channel(double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            double v = t < 1.0 / 6 ? p + (q - p) * 6 * t
                     : t < 0.5 ? q
                     : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6
                     : p;
            return (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        }
        return Color.FromRgb(Channel(h + 1.0 / 3), Channel(h), Channel(h - 1.0 / 3));
    }
}
