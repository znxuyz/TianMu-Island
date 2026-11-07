using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using TianMu.Modules.Convert.Furnace;

namespace TianMu.Modules.Convert.Engines;

/// <summary>
/// 影音轉換（FFmpeg，以獨立程式執行）。FFmpeg 不隨天幕島附帶：第一次用到時才下載（約 92 MB），
/// 下載後比對 SHA-256，放在天幕島自己的資料夾；電腦上已裝了 FFmpeg（在 PATH 上）就直接用。
/// </summary>
internal sealed partial class MediaEngine(string engineDir)
{
    // gyan.dev 的 Windows 版（GPL-3.0），經由它在 GitHub 的發佈頁下載；版本與雜湊值固定，避免被換掉
    internal const string DownloadUrl = "https://github.com/GyanD/codexffmpeg/releases/download/7.1.1/ffmpeg-7.1.1-essentials_build.zip";
    internal const string DownloadSha256 = "04861d3339c5ebe38b56c19a15cf2c0cc97f5de4fa8910e4d47e5e6404e4a2d4";
    private const long DownloadBytes = 92_234_348;

    private string LocalExe => Path.Combine(engineDir, "ffmpeg.exe");

    /// <summary>找得到 FFmpeg 嗎（自己的或電腦上的）。</summary>
    public bool IsReady => FindExe() is not null;

    /// <summary>這台電腦能自動下載嗎（目前只有 x64 版）。</summary>
    public static bool CanDownload => RuntimeInformation.ProcessArchitecture == Architecture.X64;

    private string? FindExe()
    {
        if (File.Exists(LocalExe)) return LocalExe;
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(dir.Trim('"'), "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* PATH 裡奇怪的項目 */ }
        }
        return null;
    }

    /// <summary>確保有 FFmpeg；沒有就下載（進度 0~1）。</summary>
    public async Task<string> EnsureAsync(IProgress<double> progress, CancellationToken ct)
    {
        if (FindExe() is { } exe) return exe;
        if (!CanDownload) throw new InvalidOperationException("這台電腦（ARM 版 Windows）需要自行安裝 FFmpeg，並加到 PATH");

        Directory.CreateDirectory(engineDir);
        string zip = Path.Combine(engineDir, "ffmpeg-download.zip");
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TianMuIsland");
            using var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? DownloadBytes;
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(zip);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                progress.Report(Math.Min(1, (double)done / total));
            }
        }

        try
        {
            await using (var check = File.OpenRead(zip))
            {
                string hash = System.Convert.ToHexStringLower(await SHA256.HashDataAsync(check, ct));
                if (hash != DownloadSha256) throw new InvalidOperationException("下載的 FFmpeg 檔案驗證不符，已刪除");
            }
            using var archive = ZipFile.OpenRead(zip);
            var entry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException("下載的 FFmpeg 裡找不到 ffmpeg.exe");
            string temp = LocalExe + ".tmp";
            entry.ExtractToFile(temp, overwrite: true);
            File.Move(temp, LocalExe, overwrite: true);
        }
        finally
        {
            try { File.Delete(zip); } catch { /* 下次再清 */ }
        }
        return LocalExe;
    }

    /// <summary>轉一個檔案。回傳成品路徑。</summary>
    public async Task<string> ConvertAsync(string source, string outDir, string op, IProgress<double> progress, CancellationToken ct)
    {
        string exe = await EnsureAsync(new Progress<double>(_ => { }), ct);
        var (ext, args, suffix) = MediaArgs.For(op, source);
        string path = OutputNames.Unique(outDir, OutputNames.For(source, ext, suffix), File.Exists);

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string a in new[] { "-hide_banner", "-nostdin", "-y", "-i", source }) psi.ArgumentList.Add(a);
        foreach (string a in args) psi.ArgumentList.Add(a);
        foreach (string a in new[] { "-progress", "pipe:1", "-nostats", path }) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("無法啟動 FFmpeg");
        double durationUs = 0;
        var errorTail = new Queue<string>();
        var stderr = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync(ct) is { } line)
            {
                if (durationUs <= 0 && MediaArgs.ParseDuration(line) is { } d) durationUs = d;
                errorTail.Enqueue(line);
                if (errorTail.Count > 6) errorTail.Dequeue();
            }
        }, ct);

        using var reg = ct.Register(() => { try { process.Kill(entireProcessTree: true); } catch { } });
        while (await process.StandardOutput.ReadLineAsync(ct) is { } line)
        {
            if (line.StartsWith("out_time_us=", StringComparison.Ordinal) && durationUs > 0 &&
                long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out long us))
                progress.Report(Math.Clamp(us / durationUs, 0, 1));
        }
        await process.WaitForExitAsync(ct);
        await stderr;

        if (process.ExitCode != 0)
        {
            try { File.Delete(path); } catch { }
            throw new InvalidOperationException("FFmpeg 轉換失敗：" + string.Join(" ", errorTail.TakeLast(2)));
        }
        return path;
    }
}

/// <summary>各種影音轉換對應的 FFmpeg 參數（純邏輯，方便測試）。</summary>
public static partial class MediaArgs
{
    public static (string Ext, string[] Args, string? Suffix) For(string op, string source) => op switch
    {
        "mp4" => ("mp4", ["-c:v", "libx264", "-preset", "medium", "-crf", "23", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart"], null),
        "webm" => ("webm", ["-c:v", "libvpx-vp9", "-crf", "33", "-b:v", "0", "-c:a", "libopus", "-b:a", "128k"], null),
        "gif" => ("gif", ["-vf", "fps=12,scale='min(640,iw)':-2:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse", "-loop", "0", "-an"], null),
        "720p" => ("mp4", ["-vf", "scale=-2:'min(720,ih)'", "-c:v", "libx264", "-preset", "medium", "-crf", "23", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart"], "720p"),
        "mp3" => ("mp3", ["-vn", "-c:a", "libmp3lame", "-q:a", "2"], null),
        "m4a" => ("m4a", ["-vn", "-c:a", "aac", "-b:a", "192k"], null),
        "wav" => ("wav", ["-vn", "-c:a", "pcm_s16le"], null),
        "flac" => ("flac", ["-vn", "-c:a", "flac"], null),
        "ogg" => ("ogg", ["-vn", "-c:a", "libvorbis", "-q:a", "5"], null),
        "opus" => ("opus", ["-vn", "-c:a", "libopus", "-b:a", "128k"], null),
        _ => throw new NotSupportedException(op),
    };

    /// <summary>從 FFmpeg 的輸出讀出片長（微秒），例如「Duration: 00:01:02.33」。</summary>
    public static double? ParseDuration(string line)
    {
        var m = DurationRegex().Match(line);
        if (!m.Success) return null;
        double h = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        double min = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        double s = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        return ((h * 60 + min) * 60 + s) * 1_000_000;
    }

    [GeneratedRegex(@"Duration:\s*(\d+):(\d{2}):(\d{2}(?:\.\d+)?)")]
    private static partial Regex DurationRegex();
}
