using System.IO;
using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using TianMu.Modules.Convert.Furnace;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace TianMu.Modules.Convert.Engines;

/// <summary>
/// PDF：每頁轉成圖片（Windows 內建的 PDF 繪製）、合併、拆頁（PDFsharp）。
/// </summary>
internal static class PdfEngine
{
    private const double RenderScale = 2.0; // 以兩倍大小繪製，文字才清楚

    public static async Task<IReadOnlyList<string>> ToImagesAsync(string source, string outDir, bool jpg, IProgress<double> progress, CancellationToken ct)
    {
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(source));
        var doc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
        var outputs = new List<string>();
        string stem = Path.GetFileNameWithoutExtension(source);

        for (uint i = 0; i < doc.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var page = doc.GetPage(i);
            using var stream = new InMemoryRandomAccessStream();
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Max(1, page.Size.Width * RenderScale),
                DestinationHeight = (uint)Math.Max(1, page.Size.Height * RenderScale),
                BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
            };
            await page.RenderToStreamAsync(stream, options); // 預設是 PNG

            string name = doc.PageCount > 1 ? $"{stem}-{i + 1}" : stem;
            string path = OutputNames.Unique(outDir, name + (jpg ? ".jpg" : ".png"), File.Exists);
            stream.Seek(0);
            if (jpg)
            {
                using var image = new MagickImage(stream.AsStreamForRead());
                image.Format = MagickFormat.Jpeg;
                image.Quality = 88;
                image.Write(path);
            }
            else
            {
                await using var fs = File.Create(path);
                await stream.AsStreamForRead().CopyToAsync(fs, ct);
            }
            outputs.Add(path);
            progress.Report((i + 1.0) / doc.PageCount);
        }
        return outputs;
    }

    public static string Merge(IReadOnlyList<string> sources, string outDir, IProgress<double> progress)
    {
        using var output = new PdfSharp.Pdf.PdfDocument();
        for (int i = 0; i < sources.Count; i++)
        {
            using var input = PdfReader.Open(sources[i], PdfDocumentOpenMode.Import);
            foreach (var page in input.Pages) output.AddPage(page);
            progress.Report((i + 1.0) / sources.Count);
        }
        string path = OutputNames.Unique(outDir, OutputNames.For(sources[0], "pdf", "合併"), File.Exists);
        output.Save(path);
        return path;
    }

    public static IReadOnlyList<string> Split(string source, string outDir, IProgress<double> progress)
    {
        using var input = PdfReader.Open(source, PdfDocumentOpenMode.Import);
        var outputs = new List<string>();
        string stem = Path.GetFileNameWithoutExtension(source);
        for (int i = 0; i < input.PageCount; i++)
        {
            using var one = new PdfSharp.Pdf.PdfDocument();
            one.AddPage(input.Pages[i]);
            string path = OutputNames.Unique(outDir, $"{stem}-{i + 1}.pdf", File.Exists);
            one.Save(path);
            outputs.Add(path);
            progress.Report((i + 1.0) / input.PageCount);
        }
        return outputs;
    }
}
