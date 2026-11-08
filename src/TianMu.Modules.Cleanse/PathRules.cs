using System.IO;

namespace TianMu.Modules.Cleanse;

/// <summary>路徑工具：展開 ~、環境變數與萬用字元；判斷是否在某資料夾底下、是否受保護。</summary>
internal static class PathRules
{
    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>展開開頭的 ~ 與 %環境變數%。展開不了的變數會原樣留著（呼叫端據此略過）。</summary>
    public static string Expand(string pattern, string home)
    {
        string p = pattern.StartsWith('~') ? home + pattern[1..] : pattern;
        return Environment.ExpandEnvironmentVariables(p);
    }

    /// <summary>把規則裡的路徑（可含單層萬用字元 *）展開成實際存在的路徑。</summary>
    public static IReadOnlyList<string> Resolve(string pattern, string home)
    {
        string full = Expand(pattern, home);
        if (full.Contains('%')) return [];
        string? root = System.IO.Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root)) return [];

        var segments = full[root.Length..].Split([System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        List<string> current = [root];
        foreach (var seg in segments)
        {
            var next = new List<string>();
            foreach (var dir in current)
            {
                if (seg.Contains('*') || seg.Contains('?'))
                {
                    try { next.AddRange(Directory.EnumerateDirectories(dir, seg)); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                }
                else next.Add(System.IO.Path.Combine(dir, seg));
            }
            current = next;
            if (current.Count == 0) return [];
        }
        return current.Where(p => Directory.Exists(p) || File.Exists(p)).ToList();
    }

    /// <summary>統一成完整路徑、去掉結尾的分隔符號，方便比較。</summary>
    public static string Normalize(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        return full.Length > 3 ? full.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) : full;
    }

    /// <summary>path 是否就是 root，或在 root 底下。</summary>
    public static bool IsUnder(string path, string root)
    {
        string p = Normalize(path), r = Normalize(root);
        if (string.Equals(p, r, StringComparison.OrdinalIgnoreCase)) return true;
        string prefix = r.EndsWith(System.IO.Path.DirectorySeparatorChar) ? r : r + System.IO.Path.DirectorySeparatorChar;
        return p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>在保護名單裡（或在它底下），或本身就是磁碟根目錄、使用者資料夾本身。</summary>
    public static bool IsProtected(string path, string home, IEnumerable<string>? extra = null)
    {
        string p = Normalize(path);
        if (System.IO.Path.GetPathRoot(p) is { } root && string.Equals(Normalize(root), p, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(p, Normalize(home), StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var pattern in CleanseRules.Protected.Concat(extra ?? []))
        {
            string expanded = Expand(pattern, home);
            if (expanded.Contains('%')) continue;
            if (IsUnder(p, expanded)) return true;
        }
        return false;
    }
}
