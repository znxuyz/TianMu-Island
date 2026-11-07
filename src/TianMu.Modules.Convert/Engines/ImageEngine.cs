using System.IO;
using ImageMagick;
using TianMu.Modules.Convert.Furnace;

namespace TianMu.Modules.Convert.Engines;

/// <summary>圖片互轉、縮小、灰階、合成 PDF（ImageMagick）。</summary>
internal static class ImageEngine
{
    private const uint Quality = 85;
    private const uint ShrinkTo = 1920;

    public static string Convert(string source, string outDir, string op)
    {
        using var image = new MagickImage(source);
        image.AutoOrient(); // 依照相機的方向資訊轉正

        string ext = Path.GetExtension(source).TrimStart('.').ToLowerInvariant();
        string? suffix = null;
        switch (op)
        {
            case "webp": image.Format = MagickFormat.WebP; image.Quality = 82; ext = "webp"; break;
            case "png": image.Format = MagickFormat.Png; ext = "png"; break;
            case "jpg":
                FlattenOnWhite(image);
                image.Format = MagickFormat.Jpeg; image.Quality = Quality; ext = "jpg"; break;
            case "avif": image.Format = MagickFormat.Avif; image.Quality = 60; ext = "avif"; break;
            case "ico":
                // 方形圖示，內含 16 到 256 的各種尺寸
                image.Resize(new MagickGeometry(256, 256));
                image.Extent(256, 256, Gravity.Center, MagickColors.Transparent);
                image.Settings.SetDefine(MagickFormat.Icon, "auto-resize", "256,128,64,48,32,16");
                image.Format = MagickFormat.Icon; ext = "ico"; break;
            case "shrink":
                image.Resize(new MagickGeometry(ShrinkTo, ShrinkTo) { Greater = true }); // 只縮不放大
                image.Quality = Quality; suffix = "縮小"; break;
            case "gray":
                image.Grayscale(); image.Quality = Quality; suffix = "灰階"; break;
            default:
                throw new NotSupportedException(op);
        }

        string path = OutputNames.Unique(outDir, OutputNames.For(source, ext, suffix), File.Exists);
        image.Write(path);
        return path;
    }

    /// <summary>把幾張圖片依序做成一份 PDF（每張一頁）。</summary>
    public static string ToPdf(IReadOnlyList<string> sources, string outDir, IProgress<double> progress)
    {
        using var pages = new MagickImageCollection();
        for (int i = 0; i < sources.Count; i++)
        {
            var image = new MagickImage(sources[i]);
            image.AutoOrient();
            FlattenOnWhite(image);
            image.Format = MagickFormat.Jpeg;
            image.Quality = Quality;
            pages.Add(image);
            progress.Report((i + 1.0) / (sources.Count + 1));
        }

        string name = sources.Count > 1
            ? OutputNames.For(sources[0], "pdf", $"等{sources.Count}張")
            : OutputNames.For(sources[0], "pdf");
        string path = OutputNames.Unique(outDir, name, File.Exists);
        pages.Write(path, MagickFormat.Pdf);
        return path;
    }

    /// <summary>JPG、PDF 沒有透明：透明的地方鋪白底，而不是變成黑色。</summary>
    private static void FlattenOnWhite(MagickImage image)
    {
        if (!image.HasAlpha) return;
        image.BackgroundColor = MagickColors.White;
        image.Alpha(AlphaOption.Remove);
    }
}
