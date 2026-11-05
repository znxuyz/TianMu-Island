using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TianMu.Island.Update;

/// <summary>一個可下載的新版本。</summary>
public sealed record ReleaseInfo(Version Version, string Tag, string PageUrl, string Notes, string AssetUrl, string ChecksumUrl);

/// <summary>
/// 自動更新：向 GitHub Releases 查詢最新版本、下載、驗證 SHA-256，再替換自己並重新啟動。
///
/// 安全措施：
///   · 只接受本專案 Releases 網址下的檔案
///   · 沒有對應的 .sha256 校驗檔就拒絕更新；雜湊不符就刪除下載檔
/// </summary>
public sealed partial class UpdateService : IDisposable
{
    public const string Owner = "znxuyz";
    public const string Repo = "TianMu-Island";
    private const string ApiLatest = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
    private const string AllowedDownloadPrefix = $"https://github.com/{Owner}/{Repo}/releases/download/";

    private readonly HttpClient _http;

    public UpdateService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"TianMu-Island/{CurrentVersion}");
    }

    /// <summary>目前執行中的版本。</summary>
    public static Version CurrentVersion { get; } =
        ParseVersion(Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
        ?? new Version(0, 0, 0);

    /// <summary>這個架構對應的發佈檔名。</summary>
    public static string AssetName =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? "TianMuIsland-win-arm64.exe"
            : "TianMuIsland-win-x64.exe";

    /// <summary>
    /// 是否能自我更新：用 <c>dotnet run</c> 或在 bin 資料夾裡執行的開發版不更新，以免覆蓋編譯產物。
    /// </summary>
    public static bool CanSelfUpdate
    {
        get
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;
            if (!Path.GetFileName(exe).Equals("TianMuIsland.exe", StringComparison.OrdinalIgnoreCase)) return false;
            string dir = AppContext.BaseDirectory.Replace('/', '\\');
            return !dir.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase) &&
                   !dir.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 查詢最新正式版。沒有任何發佈、或查詢失敗時回傳 null。
    /// 回傳的版本不一定比目前新，請用 <see cref="IsNewer"/> 判斷。
    /// </summary>
    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiLatest);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null; // 尚未發佈任何版本
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseRelease(doc.RootElement, AssetName);
    }

    public static bool IsNewer(ReleaseInfo release) => release.Version > CurrentVersion;

    /// <summary>
    /// 下載並驗證新版 exe，存放在目前 exe 旁邊（同一個磁碟，替換時最安全）。回傳下載檔路徑。
    /// </summary>
    public async Task<string> DownloadAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct = default)
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("找不到目前的執行檔路徑。");
        string target = exe + ".download";

        // 1. 先取得官方校驗值
        string checksumText = await _http.GetStringAsync(release.ChecksumUrl, ct);
        string expected = ParseChecksum(checksumText)
            ?? throw new InvalidDataException("校驗檔格式不正確，已取消更新。");

        // 2. 下載
        using (var response = await _http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0) progress?.Report((double)done / total.Value);
            }
        }

        // 3. 驗證
        string actual;
        await using (var file = File.OpenRead(target))
            actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct));

        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(target);
            throw new InvalidDataException("下載的檔案校驗不符，可能已損毀或遭竄改，已刪除並取消更新。");
        }

        return target;
    }

    /// <summary>
    /// 以新版替換自己並啟動新版。成功後呼叫端應立即結束程式。
    /// 執行中的 exe 無法覆寫但可以改名，所以先把自己改名為 .old，再把新版放到原位。
    /// </summary>
    public static void ApplyAndRestart(string downloadedExe)
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("找不到目前的執行檔路徑。");
        string old = exe + ".old";

        TryDelete(old);
        File.Move(exe, old);
        try
        {
            File.Move(downloadedExe, exe);
        }
        catch
        {
            File.Move(old, exe); // 還原
            throw;
        }

        Process.Start(new ProcessStartInfo(exe, "--restart --updated") { UseShellExecute = false });
    }

    /// <summary>啟動時清掉上次更新留下的舊檔。</summary>
    public static void CleanupLeftovers()
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        TryDelete(exe + ".old");
        TryDelete(exe + ".download");
    }

    // ── 解析（純函式，方便測試）────────────────────────

    [GeneratedRegex(@"^[0-9a-fA-F]{64}$")]
    private static partial Regex Sha256Hex();

    /// <summary>"v1.2.3"、"1.2.3-beta+abc" → 1.2.3；無法解析回傳 null。</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string s = text.Trim().TrimStart('v', 'V');
        int cut = s.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0) s = s[..cut];
        if (!Version.TryParse(s, out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    /// <summary>校驗檔第一個欄位就是十六進位雜湊（sha256sum 與 PowerShell 的格式皆可）。</summary>
    public static string? ParseChecksum(string text)
    {
        string first = text.Trim().Split([' ', '\t', '\r', '\n', '*'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return Sha256Hex().IsMatch(first) ? first.ToLowerInvariant() : null;
    }

    public static bool IsAllowedDownloadUrl(string url) =>
        url.StartsWith(AllowedDownloadPrefix, StringComparison.Ordinal) && !url.Contains("..", StringComparison.Ordinal);

    public static ReleaseInfo? ParseRelease(JsonElement root, string assetName)
    {
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;

        string tag = root.GetProperty("tag_name").GetString() ?? "";
        var version = ParseVersion(tag);
        if (version is null) return null;

        string? assetUrl = null, checksumUrl = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                string url = asset.GetProperty("browser_download_url").GetString() ?? "";
                if (!IsAllowedDownloadUrl(url)) continue;
                if (name.Equals(assetName, StringComparison.OrdinalIgnoreCase)) assetUrl = url;
                else if (name.Equals(assetName + ".sha256", StringComparison.OrdinalIgnoreCase)) checksumUrl = url;
            }
        }

        // 沒有校驗檔的版本一律不更新
        if (assetUrl is null || checksumUrl is null) return null;

        return new ReleaseInfo(
            version,
            tag,
            root.TryGetProperty("html_url", out var page) ? page.GetString() ?? "" : "",
            root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
            assetUrl,
            checksumUrl);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* 檔案被占用時下次再清 */ }
    }

    public void Dispose() => _http.Dispose();
}
