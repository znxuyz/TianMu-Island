using System.Diagnostics;
using System.IO;

namespace TianMu.Modules.Cleanse;

/// <summary>
/// 掃描（只讀，不刪任何東西）：
///   1. 知識庫裡的已知快取
///   2. 使用者資料夾（下載、桌面、文件、影片、音樂、圖片）裡的大檔案與大資料夾
///   3. 資源回收筒
/// 每一項都有時間上限，算不完的標記為「部分」。
/// </summary>
public sealed class DiskScanner
{
    private static readonly TimeSpan PerItemBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LargeScanBudget = TimeSpan.FromSeconds(90);
    private const long RecycleBinMin = 50 * CleanseRules.MB;

    private readonly string _home;
    private readonly Func<IReadOnlySet<string>> _processes;

    public DiskScanner(string? home = null, Func<IReadOnlySet<string>>? processes = null)
    {
        _home = home ?? PathRules.Home;
        _processes = processes ?? RunningProcesses;
    }

    /// <summary>系統碟（Windows 所在的那顆）。</summary>
    public static string SystemVolume => System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

    /// <summary>系統碟剩餘與總空間。</summary>
    public static (long Free, long Total) VolumeStatus(string? volume = null)
    {
        try
        {
            var d = new DriveInfo(volume ?? SystemVolume);
            return (d.AvailableFreeSpace, d.TotalSize);
        }
        catch { return (0, 0); }
    }

    public static IReadOnlySet<string> RunningProcesses()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try { set.Add(p.ProcessName); }
            catch { /* 已經結束 */ }
            finally { p.Dispose(); }
        }
        return set;
    }

    public ScanResult Scan(IProgress<string>? progress, CancellationToken ct, IReadOnlyList<CacheRule>? rules = null,
        bool includeLarge = true, bool includeRecycleBin = true)
    {
        rules ??= CleanseRules.Known;
        var now = DateTime.Now;
        var items = new List<CleanseItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var procs = _processes();

        // 1) 已知快取
        progress?.Report("看看常見的快取…");
        foreach (var rule in rules)
        {
            foreach (var pattern in rule.Paths)
            {
                foreach (var path in PathRules.Resolve(pattern, _home))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!seen.Add(PathRules.Normalize(path))) continue;
                    var (bytes, partial) = SizeOf(path, PerItemBudget, ct);
                    if (bytes < rule.MinMB * CleanseRules.MB) continue;
                    var apps = (rule.Apps ?? []).Where(procs.Contains).ToList();
                    bool prot = PathRules.IsProtected(path, _home);
                    var (tier, reason) = CleanseJudge.Judge(CleanseKind.KnownCache, rule.Tier, path, rule.Impact, prot, false, apps, null, now);
                    items.Add(new CleanseItem
                    {
                        Id = "", Path = path, Name = rule.Name, Kind = CleanseKind.KnownCache, Mode = rule.Mode, Tier = tier,
                        Bytes = bytes, Reason = reason, AppsRunning = apps, Partial = partial, RuleId = rule.Id,
                        WrittenAtUtc = WrittenAt(path),
                    });
                }
            }
        }

        // 2) 使用者資料夾裡的大檔案、大資料夾
        if (includeLarge)
        {
            progress?.Report("找佔空間的大檔案…");
            var deadline = DateTime.UtcNow + LargeScanBudget;
            var files = new List<(string Path, long Bytes)>();
            var folders = new List<(string Path, long Bytes)>();
            foreach (var name in CleanseRules.LargeRoots)
            {
                string root = System.IO.Path.Combine(_home, name);
                if (!Directory.Exists(root)) continue;
                Walk(root, 0, files, folders, deadline, ct);
            }

            foreach (var (path, bytes) in files)
            {
                if (!seen.Add(PathRules.Normalize(path))) continue;
                var last = WrittenAt(path)?.ToLocalTime();
                var (tier, reason) = CleanseJudge.Judge(CleanseKind.LargeFile, CleanseTier.Optional, path, "",
                    PathRules.IsProtected(path, _home), false, [], last, now);
                items.Add(new CleanseItem
                {
                    Id = "", Path = path, Name = System.IO.Path.GetFileName(path), Kind = CleanseKind.LargeFile, Tier = tier,
                    Bytes = bytes, Reason = reason, WrittenAtUtc = WrittenAt(path),
                });
            }

            progress?.Report("統計大資料夾…");
            var big = folders.Where(f => f.Bytes >= CleanseRules.LargeFolderBytes)
                // 已經逐一列出的大檔案所在的資料夾不再整包列出，避免重複計算
                .Where(f => !files.Any(x => PathRules.IsUnder(x.Path, f.Path)))
                .Where(f => !items.Any(x => PathRules.IsUnder(x.Path, f.Path)));
            foreach (var (path, bytes) in CleanseJudge.Deepest(big))
            {
                if (!seen.Add(PathRules.Normalize(path))) continue;
                var last = LastUsed(path);
                bool repo = IsRepository(path);
                var (tier, reason) = CleanseJudge.Judge(CleanseKind.LargeFolder, CleanseTier.Optional, path, "",
                    PathRules.IsProtected(path, _home), repo, [], last, now);
                items.Add(new CleanseItem
                {
                    Id = "", Path = path, Name = System.IO.Path.GetFileName(path), Kind = CleanseKind.LargeFolder, Tier = tier,
                    Bytes = bytes, Reason = reason, WrittenAtUtc = WrittenAt(path),
                });
            }
        }

        // 3) 資源回收筒
        if (includeRecycleBin)
        {
            var (bin, count) = RecycleBin.Query();
            if (bin >= RecycleBinMin)
                items.Add(new CleanseItem
                {
                    Id = "", Path = "資源回收筒", Name = $"資源回收筒（{count} 項）", Kind = CleanseKind.RecycleBin, Tier = CleanseTier.Optional,
                    Bytes = bin, Reason = "刪掉的檔案還放在這裡佔空間；清空之後就找不回來了",
                });
        }

        var (free, total) = VolumeStatus();
        var sorted = items.OrderBy(i => i.Tier).ThenByDescending(i => i.Bytes)
            .Select((it, n) => new CleanseItem
            {
                Id = $"c{n + 1:000}", Path = it.Path, Name = it.Name, Kind = it.Kind, Mode = it.Mode, Tier = it.Tier, Bytes = it.Bytes,
                Reason = it.Reason, AppsRunning = it.AppsRunning, WrittenAtUtc = it.WrittenAtUtc, Partial = it.Partial, RuleId = it.RuleId,
            }).ToList();
        return new ScanResult(SystemVolume, free, total, sorted, now);
    }

    // ═══════════════════════ 走訪 ═══════════════════════

    /// <summary>走訪一個使用者資料夾：記下大檔案，並記錄第 1、2 層子資料夾的大小。回傳這一層的總大小。</summary>
    private static long Walk(string dir, int depth, List<(string, long)> files, List<(string, long)> folders, DateTime deadline, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (DateTime.UtcNow > deadline) return 0;
        long total = 0;
        IEnumerable<FileSystemInfo> entries;
        try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", Shallow).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }

        foreach (var e in entries)
        {
            if (e is FileInfo f)
            {
                long len;
                try { len = f.Length; }
                catch (IOException) { continue; }
                total += len;
                if (len >= CleanseRules.LargeFileBytes && !f.Attributes.HasFlag(FileAttributes.Hidden) && !f.Attributes.HasFlag(FileAttributes.System))
                    files.Add((f.FullName, len));
            }
            else if (e is DirectoryInfo d)
            {
                if (d.Name.StartsWith('.') || CleanseRules.SkipFolderNames.Contains(d.Name)) continue;
                long sub = Walk(d.FullName, depth + 1, files, folders, deadline, ct);
                total += sub;
                if (depth + 1 <= 2) folders.Add((d.FullName, sub));
            }
        }
        return total;
    }

    private static readonly EnumerationOptions Shallow = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint,   // 不跟著捷徑、連結跑到別處
    };

    private static readonly EnumerationOptions Deep = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <summary>一個檔案或資料夾的大小（有時間上限；超時或進不去的地方標記為部分）。</summary>
    internal static (long Bytes, bool Partial) SizeOf(string path, TimeSpan budget, CancellationToken ct)
    {
        try
        {
            if (File.Exists(path)) return (new FileInfo(path).Length, false);
            if (!Directory.Exists(path)) return (0, false);
            var deadline = DateTime.UtcNow + budget;
            long total = 0;
            int n = 0;
            foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", Deep))
            {
                try { total += f.Length; }
                catch (IOException) { }
                if (++n % 512 == 0)
                {
                    ct.ThrowIfCancellationRequested();
                    if (DateTime.UtcNow > deadline) return (total, true);
                }
            }
            return (total, false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (0, true);
        }
    }

    internal static DateTime? WrittenAt(string path)
    {
        try
        {
            if (File.Exists(path)) return File.GetLastWriteTimeUtc(path);
            if (Directory.Exists(path)) return Directory.GetLastWriteTimeUtc(path);
        }
        catch { }
        return null;
    }

    /// <summary>資料夾最後被用的時間：看自己和第一層內容的修改時間（夠快也夠準）。</summary>
    private static DateTime? LastUsed(string dir)
    {
        try
        {
            var latest = Directory.GetLastWriteTime(dir);
            foreach (var e in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", Shallow).Take(500))
                if (e.LastWriteTime > latest) latest = e.LastWriteTime;
            return latest;
        }
        catch { return null; }
    }

    /// <summary>版本庫或程式專案：.git 本身，或裡面有 .git / .svn / .hg。</summary>
    internal static bool IsRepository(string dir)
    {
        string name = System.IO.Path.GetFileName(dir);
        if (name is ".git" or ".svn" or ".hg") return true;
        return Directory.Exists(System.IO.Path.Combine(dir, ".git")) || Directory.Exists(System.IO.Path.Combine(dir, ".svn"))
            || Directory.Exists(System.IO.Path.Combine(dir, ".hg"));
    }
}
