using System.IO;
using System.Text.Json;

namespace TianMu.Modules.Shelf;

/// <summary>
/// 乾坤袋裡的檔案清單。只記住「路徑」，不複製檔案本身——檔案仍在原處。
/// 最多放 24 件，太多時最舊的先離開。
/// </summary>
public sealed class ShelfStore
{
    public const int Capacity = 24;

    private readonly List<string> _paths = [];

    public IReadOnlyList<string> Paths => _paths;

    /// <summary>放入檔案；已在袋中的不重複放。回傳實際新增的數量。</summary>
    public int Add(IEnumerable<string> paths)
    {
        int added = 0;
        foreach (string raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string path = raw.TrimEnd('\\', '/');
            if (_paths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase))) continue;
            _paths.Insert(0, path);
            added++;
        }
        while (_paths.Count > Capacity) _paths.RemoveAt(_paths.Count - 1);
        return added;
    }

    public bool Remove(string path) =>
        _paths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0;

    public void Clear() => _paths.Clear();

    /// <summary>移除已經不存在（被刪除或搬走）的檔案。回傳移除的數量。</summary>
    public int Prune(Func<string, bool> exists) => _paths.RemoveAll(p => !exists(p));

    // ── 存檔 ──────────────────────────────────

    public static ShelfStore Load(string file)
    {
        var store = new ShelfStore();
        try
        {
            if (File.Exists(file) && JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file)) is { } list)
                store._paths.AddRange(list.Take(Capacity));
        }
        catch { /* 損毀就從空的開始 */ }
        return store;
    }

    public void Save(string file)
    {
        string temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_paths));
        File.Move(temp, file, overwrite: true);
    }
}
