namespace TianMu.Modules.Cleanse;

/// <summary>清理的三個等級。</summary>
public enum CleanseTier
{
    /// <summary>可清：會自動重建的快取或暫存，預設勾選、直接刪除。</summary>
    Primary,
    /// <summary>你決定：使用者自己的檔案，或重建代價高的東西；預設不勾，清理時移到資源回收筒。</summary>
    Optional,
    /// <summary>不動：受保護、版本庫、正在進行中的專案，只列出來讓你知道佔了多少。</summary>
    Keep,
}

/// <summary>清的是資料夾裡的東西（保留資料夾本身），還是整個刪掉。</summary>
public enum CleanseMode { Contents, Whole }

/// <summary>找到的東西是哪一類。</summary>
public enum CleanseKind { KnownCache, LargeFile, LargeFolder, RecycleBin }

/// <summary>一條已知快取的規則。路徑可用環境變數（%LOCALAPPDATA%）、~（使用者資料夾）與單層萬用字元 *。</summary>
public sealed record CacheRule(
    string Id,
    string Name,
    IReadOnlyList<string> Paths,
    CleanseMode Mode,
    CleanseTier Tier,
    string Impact,
    IReadOnlyList<string>? Apps = null,
    int MinMB = 50);

/// <summary>
/// 洗髓的知識庫：Windows 上常見、可以安全清理的快取位置。
/// 只收「刪了會自己重建」或「明確是暫存」的東西；拿不準的一律放「你決定」或不收。
/// </summary>
public static class CleanseRules
{
    public const long MB = 1024 * 1024;
    public const long GB = 1024 * MB;

    /// <summary>單一大檔案的門檻。</summary>
    public const long LargeFileBytes = 500 * MB;

    /// <summary>大資料夾的門檻。</summary>
    public const long LargeFolderBytes = 1 * GB;

    /// <summary>找大檔案、大資料夾的地方（使用者資料夾底下）。</summary>
    public static readonly IReadOnlyList<string> LargeRoots = ["Downloads", "Desktop", "Documents", "Videos", "Music", "Pictures"];

    private const string Browser = "網頁快取，關掉瀏覽器後會自動重建；書籤、密碼、登入狀態都不受影響";
    private const string Editor = "編輯器的快取，設定與擴充功能不受影響";
    private const string Packages = "套件下載快取，下次安裝時重新下載";

    public static readonly IReadOnlyList<CacheRule> Known =
    [
        // 瀏覽器
        new("chrome", "Chrome 網頁快取",
            [@"%LOCALAPPDATA%\Google\Chrome\User Data\*\Cache", @"%LOCALAPPDATA%\Google\Chrome\User Data\*\Code Cache", @"%LOCALAPPDATA%\Google\Chrome\User Data\*\GPUCache"],
            CleanseMode.Contents, CleanseTier.Primary, Browser, ["chrome"]),
        new("edge", "Edge 網頁快取",
            [@"%LOCALAPPDATA%\Microsoft\Edge\User Data\*\Cache", @"%LOCALAPPDATA%\Microsoft\Edge\User Data\*\Code Cache", @"%LOCALAPPDATA%\Microsoft\Edge\User Data\*\GPUCache"],
            CleanseMode.Contents, CleanseTier.Primary, Browser, ["msedge"]),
        new("brave", "Brave 網頁快取",
            [@"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data\*\Cache", @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data\*\Code Cache"],
            CleanseMode.Contents, CleanseTier.Primary, Browser, ["brave"]),
        new("firefox", "Firefox 網頁快取", [@"%LOCALAPPDATA%\Mozilla\Firefox\Profiles\*\cache2"],
            CleanseMode.Contents, CleanseTier.Primary, Browser, ["firefox"]),

        // 通訊與娛樂
        new("discord", "Discord 快取", [@"%APPDATA%\discord\Cache", @"%APPDATA%\discord\Code Cache", @"%APPDATA%\discord\GPUCache"],
            CleanseMode.Contents, CleanseTier.Primary, "圖片與介面快取，聊天紀錄存在雲端，不受影響", ["Discord"]),
        new("steam-web", "Steam 內建網頁快取", [@"%LOCALAPPDATA%\Steam\htmlcache"],
            CleanseMode.Contents, CleanseTier.Primary, "商店與社群頁面的快取；遊戲本體與存檔不受影響", ["steam", "steamwebhelper"]),
        new("spotify", "Spotify 串流快取", [@"%LOCALAPPDATA%\Spotify\Data"],
            CleanseMode.Contents, CleanseTier.Optional, "聽過的歌暫存；刪了要重新串流，若有下載離線歌曲也可能要重下", ["Spotify"]),

        // 開發工具
        new("npm", "npm 套件快取", [@"%LOCALAPPDATA%\npm-cache\_cacache", @"%LOCALAPPDATA%\npm-cache\_npx"],
            CleanseMode.Whole, CleanseTier.Primary, Packages),
        new("yarn", "Yarn 套件快取", [@"%LOCALAPPDATA%\Yarn\Cache"], CleanseMode.Whole, CleanseTier.Primary, Packages),
        new("pnpm", "pnpm 共用套件庫", [@"%LOCALAPPDATA%\pnpm\store"],
            CleanseMode.Whole, CleanseTier.Optional, "所有專案共用的套件庫；刪了之後各專案要重新安裝相依套件"),
        new("pip", "pip 套件快取", [@"%LOCALAPPDATA%\pip\Cache"], CleanseMode.Whole, CleanseTier.Primary, Packages),
        new("uv", "uv 套件快取", [@"%LOCALAPPDATA%\uv\cache"], CleanseMode.Whole, CleanseTier.Primary, Packages),
        new("nuget-http", "NuGet 下載快取", [@"%LOCALAPPDATA%\NuGet\v3-cache", @"%LOCALAPPDATA%\NuGet\plugins-cache"],
            CleanseMode.Whole, CleanseTier.Primary, Packages),
        new("nuget-packages", "NuGet 全域套件", [@"~\.nuget\packages"],
            CleanseMode.Whole, CleanseTier.Optional, "所有 .NET 專案共用的套件；刪了下次建置會重新下載，比較慢"),
        new("gradle", "Gradle 快取", [@"~\.gradle\caches"], CleanseMode.Whole, CleanseTier.Primary, "相依套件與建置快取，下次建置會重新下載"),
        new("maven", "Maven 本機套件庫", [@"~\.m2\repository"], CleanseMode.Whole, CleanseTier.Optional, "可重新下載，但下次建置會慢很多"),
        new("cargo", "Cargo 下載快取", [@"~\.cargo\registry\cache"], CleanseMode.Whole, CleanseTier.Primary, Packages),
        new("go-build", "Go 編譯快取", [@"%LOCALAPPDATA%\go-build"], CleanseMode.Whole, CleanseTier.Primary, "編譯快取，下次編譯會重建"),
        new("vscode", "VS Code 快取", [@"%APPDATA%\Code\Cache", @"%APPDATA%\Code\CachedData", @"%APPDATA%\Code\CachedExtensionVSIXs"],
            CleanseMode.Contents, CleanseTier.Primary, Editor, ["Code"]),
        new("cursor", "Cursor 快取", [@"%APPDATA%\Cursor\Cache", @"%APPDATA%\Cursor\CachedData"],
            CleanseMode.Contents, CleanseTier.Primary, Editor, ["Cursor"]),
        new("jetbrains", "JetBrains IDE 索引快取", [@"%LOCALAPPDATA%\JetBrains\*\caches"],
            CleanseMode.Whole, CleanseTier.Primary, "IDE 的索引，下次開專案會重建（會慢一下）"),
        new("playwright", "Playwright 測試用瀏覽器", [@"%LOCALAPPDATA%\ms-playwright"],
            CleanseMode.Whole, CleanseTier.Optional, "自動化測試用的瀏覽器，需要時會重新下載（很大）"),

        // 系統暫存
        new("temp", "暫存檔", [@"%TEMP%"], CleanseMode.Contents, CleanseTier.Primary, "程式執行時留下的暫存檔；正在使用的會自動略過", MinMB: 100),
        new("crash-dumps", "程式當機紀錄", [@"%LOCALAPPDATA%\CrashDumps"], CleanseMode.Contents, CleanseTier.Primary, "程式當機時留下的診斷檔，刪了不影響使用"),
        new("wer", "Windows 錯誤報告", [@"%LOCALAPPDATA%\Microsoft\Windows\WER\ReportArchive", @"%LOCALAPPDATA%\Microsoft\Windows\WER\ReportQueue"],
            CleanseMode.Contents, CleanseTier.Primary, "已送出或待送出的錯誤報告，刪了不影響使用"),
        new("shader", "顯示卡著色器快取", [@"%LOCALAPPDATA%\D3DSCache", @"%LOCALAPPDATA%\NVIDIA\DXCache", @"%LOCALAPPDATA%\NVIDIA\GLCache", @"%LOCALAPPDATA%\AMD\DxCache"],
            CleanseMode.Contents, CleanseTier.Primary, "遊戲與繪圖程式的著色器快取，下次開啟時重建（第一次會稍微卡一下）"),
    ];

    /// <summary>永遠不清理的地方（以及它們底下的一切）。</summary>
    public static readonly IReadOnlyList<string> Protected =
    [
        @"~\.ssh", @"~\.gnupg", @"~\.claude", @"~\.codex", @"~\.config", @"~\OneDrive",
        @"%APPDATA%\Microsoft\Credentials", @"%APPDATA%\Microsoft\Protect", @"%LOCALAPPDATA%\Microsoft\Credentials",
        @"%SystemRoot%", @"%ProgramFiles%", @"%ProgramFiles(x86)%", @"%ProgramData%",
    ];

    /// <summary>找大檔案、大資料夾時不進去的資料夾名稱。</summary>
    public static readonly IReadOnlySet<string> SkipFolderNames =
        new HashSet<string>(["node_modules", "AppData", "$RECYCLE.BIN", "System Volume Information"], StringComparer.OrdinalIgnoreCase);

    /// <summary>安裝檔、壓縮檔、光碟映像：裝好之後通常就用不到了。</summary>
    public static readonly IReadOnlySet<string> InstallerExtensions =
        new HashSet<string>([".exe", ".msi", ".msix", ".zip", ".7z", ".rar", ".iso", ".img"], StringComparer.OrdinalIgnoreCase);
}
