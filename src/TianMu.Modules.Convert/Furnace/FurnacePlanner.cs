using System.IO;
using TianMu.Core;

namespace TianMu.Modules.Convert.Furnace;

/// <summary>檔案的大類（依副檔名判斷）。</summary>
public enum FileKind { Other, Image, Pdf, Video, Audio, Subtitle, Text, Archive }

/// <summary>
/// 決定一批檔案能轉成什麼（純邏輯，方便測試）。同一類的檔案給該類的格式；混在一起只能打包。
/// 每一類最多八種，最常用的排前面（法陣盤從正上方順時針排）。
/// </summary>
public static class FurnacePlanner
{
    private static readonly Dictionary<string, FileKind> Kinds = Build(
        (FileKind.Image, "jpg jpeg jfif png webp avif heic heif gif bmp tif tiff ico jxl psd tga qoi dds"),
        (FileKind.Pdf, "pdf"),
        (FileKind.Video, "mp4 mov mkv webm avi wmv flv m4v mpg mpeg ts m2ts 3gp ogv"),
        (FileKind.Audio, "mp3 m4a aac wav flac ogg opus wma aiff aif amr"),
        (FileKind.Subtitle, "srt vtt"),
        (FileKind.Text, "txt log csv md ini"),
        (FileKind.Archive, "zip"));

    private static Dictionary<string, FileKind> Build(params (FileKind Kind, string Exts)[] groups)
    {
        var map = new Dictionary<string, FileKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var (kind, exts) in groups)
            foreach (string e in exts.Split(' ')) map["." + e] = kind;
        return map;
    }

    public static FileKind KindOf(string path) =>
        Kinds.TryGetValue(Path.GetExtension(path), out var kind) ? kind : FileKind.Other;

    /// <summary>這批檔案共同的類別；混合的回傳 <see cref="FileKind.Other"/>。</summary>
    public static FileKind CommonKind(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return FileKind.Other;
        var first = KindOf(files[0]);
        return files.All(f => KindOf(f) == first) ? first : FileKind.Other;
    }

    public static IReadOnlyList<FurnaceOption> OptionsFor(IReadOnlyList<string> files, bool mediaReady)
    {
        if (files.Count == 0) return [];
        bool many = files.Count > 1;
        string media = mediaReady ? "" : " · 首次需下載";
        var kind = CommonKind(files);

        List<FurnaceOption> list = kind switch
        {
            FileKind.Image =>
            [
                new("img:webp", "WEBP", "最小"),
                new("img:png", "PNG", "無損"),
                new("img:jpg", "JPG", "通用"),
                new("img:pdf", "PDF", many ? "合成一份" : "做成文件", "ZhuShaBrush"),
                new("img:avif", "AVIF", "新格式"),
                new("img:ico", "ICO", "圖示"),
                new("img:shrink", "縮小", "長邊 1920", "TianQingBrush"),
                new("img:gray", "灰階", "黑白", "TianQingBrush"),
            ],
            FileKind.Pdf =>
            [
                new("pdf:png", "PNG", "每頁一張"),
                new("pdf:jpg", "JPG", "每頁一張"),
                .. many ? new FurnaceOption[] { new("pdf:merge", "合併", "依序合成一份", "ZhuShaBrush") } : [],
                new("pdf:split", "拆頁", "每頁一個 PDF", "TianQingBrush"),
                new("zip:pack", "ZIP", "打包"),
            ],
            FileKind.Video =>
            [
                new("av:mp4", "MP4", "通用" + media),
                new("av:webm", "WEBM", "網頁" + media),
                new("av:gif", "GIF", "動圖" + media),
                new("av:mp3", "MP3", "只要聲音" + media, "TianQingBrush"),
                new("av:720p", "720p", "縮小畫面" + media, "TianQingBrush"),
                new("av:m4a", "M4A", "只要聲音" + media),
            ],
            FileKind.Audio =>
            [
                new("au:mp3", "MP3", "通用" + media),
                new("au:m4a", "M4A", "AAC" + media),
                new("au:wav", "WAV", "無壓縮" + media),
                new("au:flac", "FLAC", "無損" + media),
                new("au:ogg", "OGG", "Vorbis" + media),
                new("au:opus", "OPUS", "最小" + media),
            ],
            FileKind.Subtitle =>
            [
                new("sub:srt", "SRT", "通用字幕"),
                new("sub:vtt", "VTT", "網頁字幕"),
                new("txt:utf8", "UTF-8", "修正亂碼", "TianQingBrush"),
            ],
            FileKind.Text =>
            [
                new("txt:utf8", "UTF-8", "修正亂碼", "TianQingBrush"),
                new("zip:pack", "ZIP", "打包"),
            ],
            FileKind.Archive =>
            [
                new("zip:extract", "解壓", "放進資料夾", "TianQingBrush"),
            ],
            _ =>
            [
                new("zip:pack", "ZIP", many ? "全部打包" : "壓縮"),
            ],
        };

        // 同格式互轉沒有意義（例如 PNG 轉 PNG），但「縮小」「灰階」保留
        if (kind is FileKind.Image or FileKind.Audio)
        {
            var exts = files.Select(f => Path.GetExtension(f).TrimStart('.').ToLowerInvariant()).Distinct().ToList();
            if (exts.Count == 1)
            {
                string same = exts[0] == "jpeg" ? "jpg" : exts[0];
                list.RemoveAll(o => o.Id.EndsWith(":" + same, StringComparison.Ordinal));
            }
        }
        return list.Take(8).ToList();
    }
}

/// <summary>成品的檔名與放置位置（純邏輯，方便測試）。</summary>
public static class OutputNames
{
    /// <summary>「trip.jpg」轉 webp → 「trip.webp」；加上後綴時 → 「trip-灰階.jpg」。</summary>
    public static string For(string source, string extension, string? suffix = null)
    {
        string name = Path.GetFileNameWithoutExtension(source);
        if (!string.IsNullOrEmpty(suffix)) name += "-" + suffix;
        return name + (extension.StartsWith('.') ? extension : "." + extension);
    }

    /// <summary>資料夾裡已有同名檔案時加上編號：trip.webp → trip (2).webp。</summary>
    public static string Unique(string folder, string fileName, Func<string, bool> exists)
    {
        string path = Path.Combine(folder, fileName);
        if (!exists(path)) return path;
        string stem = Path.GetFileNameWithoutExtension(fileName), ext = Path.GetExtension(fileName);
        for (int i = 2; i < 10000; i++)
        {
            path = Path.Combine(folder, $"{stem} ({i}){ext}");
            if (!exists(path)) return path;
        }
        return Path.Combine(folder, $"{stem} ({Guid.NewGuid():N}){ext}");
    }
}
