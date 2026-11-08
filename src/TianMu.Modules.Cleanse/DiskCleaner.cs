using System.IO;

namespace TianMu.Modules.Cleanse;

/// <summary>
/// 執行清理：只處理使用者勾選的項目，每一項動手前都再複核一次——
///   · 還在不在（不在就略過）
///   · 不在保護名單、等級不是「不動」
///   · 使用者的檔案在掃描之後沒被修改過（被改過代表還在用，先不碰）
/// 「你決定」的項目預設移到資源回收筒，可以找回來；正在被使用、刪不掉的檔案會略過，不中斷整批。
/// </summary>
public static class DiskCleaner
{
    private static readonly TimeSpan ModifiedTolerance = TimeSpan.FromSeconds(2);

    /// <summary>動手前的複核：回傳不能清的原因，可以清時回傳 null。</summary>
    public static string? Verify(CleanseItem item, string home)
    {
        if (item.Tier == CleanseTier.Keep) return "這一項不會清理";
        if (item.Kind == CleanseKind.RecycleBin) return null;
        if (!File.Exists(item.Path) && !Directory.Exists(item.Path)) return "已經不在了";
        if (PathRules.IsProtected(item.Path, home)) return "在保護名單裡，略過";
        if (item.Kind is CleanseKind.LargeFile or CleanseKind.LargeFolder && item.WrittenAtUtc is { } scanned
            && DiskScanner.WrittenAt(item.Path) is { } current && (current - scanned).Duration() > ModifiedTolerance)
            return "掃描之後被修改過，為了安全先略過；重新掃描再試";
        return null;
    }

    public static CleanResult Clean(CleanseItem item, bool optionalToRecycleBin, CancellationToken ct, string? home = null)
    {
        home ??= PathRules.Home;
        string? problem = Verify(item, home);
        if (problem is not null)
            return new(item, problem == "已經不在了" ? CleanOutcome.Gone : CleanOutcome.Skipped, 0, problem);

        try
        {
            if (item.Kind == CleanseKind.RecycleBin)
            {
                var (before, _) = RecycleBin.Query();
                return RecycleBin.Empty()
                    ? new(item, CleanOutcome.Deleted, before, "資源回收筒已清空")
                    : new(item, CleanOutcome.Failed, 0, "清空資源回收筒失敗");
            }

            var (sizeBefore, _) = DiskScanner.SizeOf(item.Path, TimeSpan.FromSeconds(30), ct);

            bool recycle = item.Tier == CleanseTier.Optional && optionalToRecycleBin;
            if (recycle)
            {
                RecycleBin.Send(item.Path);
                return File.Exists(item.Path) || Directory.Exists(item.Path)
                    ? new(item, CleanOutcome.Failed, 0, "沒能移到資源回收筒（可能正在使用）")
                    : new(item, CleanOutcome.Recycled, sizeBefore, "已移到資源回收筒；清空回收筒後才會真正釋放空間");
            }

            int failures = 0;
            if (File.Exists(item.Path)) DeleteFile(item.Path, ref failures);
            else if (item.Mode == CleanseMode.Contents)
            {
                foreach (var entry in new DirectoryInfo(item.Path).EnumerateFileSystemInfos())
                {
                    ct.ThrowIfCancellationRequested();
                    DeleteEntry(entry, ref failures, ct);
                }
            }
            else DeleteEntry(new DirectoryInfo(item.Path), ref failures, ct);

            var (sizeAfter, _) = File.Exists(item.Path) || Directory.Exists(item.Path)
                ? DiskScanner.SizeOf(item.Path, TimeSpan.FromSeconds(30), ct) : (0, false);
            long freed = Math.Max(0, sizeBefore - sizeAfter);
            if (failures == 0) return new(item, CleanOutcome.Deleted, freed, "已清理");
            return freed > 0
                ? new(item, CleanOutcome.Partial, freed, $"有 {failures} 個檔案正在使用或受保護，留著沒刪，其餘已清理")
                : new(item, CleanOutcome.Failed, 0, $"有 {failures} 個檔案正在使用或受保護，沒能刪除");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            return new(item, CleanOutcome.Failed, 0, $"失敗：{e.Message}");
        }
    }

    private static void DeleteEntry(FileSystemInfo entry, ref int failures, CancellationToken ct)
    {
        if (entry is FileInfo f) { DeleteFile(f.FullName, ref failures); return; }
        if (entry is not DirectoryInfo d) return;

        // 連結（junction、symlink）只拿掉連結本身，絕不跟進去刪目標
        if (d.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            try { d.Delete(false); } catch { failures++; }
            return;
        }
        IEnumerable<FileSystemInfo> children;
        try { children = d.EnumerateFileSystemInfos().ToList(); }
        catch { failures++; return; }
        foreach (var c in children)
        {
            ct.ThrowIfCancellationRequested();
            DeleteEntry(c, ref failures, ct);
        }
        try { d.Delete(false); } catch { failures++; }
    }

    private static void DeleteFile(string path, ref int failures)
    {
        try
        {
            File.SetAttributes(path, FileAttributes.Normal);   // 唯讀的暫存檔也刪得掉
            File.Delete(path);
        }
        catch { failures++; }
    }
}
