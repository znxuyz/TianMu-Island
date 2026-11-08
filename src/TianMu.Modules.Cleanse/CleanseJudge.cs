namespace TianMu.Modules.Cleanse;

/// <summary>
/// 決定每一項的等級與說明（純邏輯，方便測試）。保守為先：
///   · 受保護的一律不動；版本庫、專案資料夾不動
///   · 使用者自己的檔案最多只到「你決定」，絕不會預設勾選
///   · 相關程式還開著時，提醒先關掉
/// </summary>
public static class CleanseJudge
{
    public static (CleanseTier Tier, string Reason) Judge(
        CleanseKind kind, CleanseTier suggested, string path, string impact,
        bool isProtected, bool isRepository, IReadOnlyList<string> appsRunning, DateTime? lastUsed, DateTime now)
    {
        if (isProtected) return (CleanseTier.Keep, "在保護名單裡，不會清理");
        if (isRepository) return (CleanseTier.Keep, "版本庫或專案資料夾，刪了會失去歷史紀錄");

        var tier = suggested;
        string reason = impact;
        if (kind is CleanseKind.LargeFile or CleanseKind.LargeFolder)
        {
            if (tier == CleanseTier.Primary) tier = CleanseTier.Optional;   // 使用者的檔案絕不預設勾選
            string age = lastUsed is { } t ? $"{Math.Max(0, (int)(now - t).TotalDays)} 天沒動過" : "不確定多久沒用";
            bool installer = kind == CleanseKind.LargeFile && CleanseRules.InstallerExtensions.Contains(System.IO.Path.GetExtension(path));
            reason = installer
                ? $"安裝檔或壓縮檔，{age}；裝好之後通常就用不到了"
                : kind == CleanseKind.LargeFile ? $"你的大檔案，{age}；確定不要了再刪" : $"你的大資料夾，{age}；確定不要了再刪";
        }
        if (appsRunning.Count > 0) reason += $"（{string.Join("、", appsRunning)} 還開著，建議先關掉）";
        return (tier, reason);
    }

    /// <summary>位元組換成好讀的大小。</summary>
    public static string Size(long bytes) => bytes switch
    {
        >= CleanseRules.GB => $"{bytes / (double)CleanseRules.GB:0.0} GB",
        >= CleanseRules.MB => $"{bytes / (double)CleanseRules.MB:0} MB",
        >= 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} B",
    };

    /// <summary>從一堆大資料夾中只留最深的那層（父子都很大時，只列子資料夾，避免重複計算）。</summary>
    public static IReadOnlyList<(string Path, long Bytes)> Deepest(IEnumerable<(string Path, long Bytes)> folders)
    {
        var chosen = new List<(string Path, long Bytes)>();
        foreach (var f in folders.OrderByDescending(f => f.Path.Length))
        {
            if (chosen.Any(c => PathRules.IsUnder(c.Path, f.Path))) continue;
            chosen.Add(f);
        }
        return chosen;
    }
}
