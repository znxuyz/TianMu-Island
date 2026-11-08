namespace TianMu.Island.Shell;

/// <summary>
/// 法陣盤要放哪些扇區、什麼順序（純邏輯，方便測試）。
/// 使用者可以自訂 4 到 10 格；自訂的扇區所屬法器被關掉時，從預設的扇區依序補足到 4 格。
/// </summary>
public static class RadialLayout
{
    public const int Min = 4;
    public const int Max = 10;

    /// <summary>一個可放上法陣盤的扇區（候選）。</summary>
    public readonly record struct Candidate(string Key, int Order, bool DefaultVisible);

    /// <summary>
    /// 從所有候選中排出法陣盤上的扇區鍵。
    /// <paramref name="chosen"/> 為 null 時用預設（DefaultVisible 的依 Order 排），否則照使用者的順序；
    /// 結果一律介於 <see cref="Min"/> 與 <see cref="Max"/> 之間（候選本身不足 4 個時就全部放上）。
    /// </summary>
    public static IReadOnlyList<string> Arrange(IEnumerable<Candidate> candidates, IReadOnlyList<string>? chosen)
    {
        var all = candidates.GroupBy(c => c.Key).Select(g => g.First()).OrderBy(c => c.Order).ToList();
        var keys = new HashSet<string>(all.Select(c => c.Key));

        List<string> result = chosen is null
            ? all.Where(c => c.DefaultVisible).Select(c => c.Key).ToList()
            : chosen.Where(keys.Contains).Distinct().ToList();

        if (result.Count > Max) result = result.Take(Max).ToList();

        // 不足 4 格：先補預設的，再補其他的
        foreach (var c in all.OrderBy(c => c.DefaultVisible ? 0 : 1).ThenBy(c => c.Order))
        {
            if (result.Count >= Min) break;
            if (!result.Contains(c.Key)) result.Add(c.Key);
        }
        return result;
    }

    /// <summary>在已選清單中加入或移除一格；違反 4~10 的限制時不變並回傳 false。</summary>
    public static bool TryToggle(List<string> chosen, string key, bool on)
    {
        if (on)
        {
            if (chosen.Contains(key)) return true;
            if (chosen.Count >= Max) return false;
            chosen.Add(key);
            return true;
        }
        if (!chosen.Contains(key)) return true;
        if (chosen.Count <= Min) return false;
        chosen.Remove(key);
        return true;
    }
}
