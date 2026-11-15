using System.IO;
using System.Text.Json;

namespace TianMu.Modules.Wenfang.Shared;

/// <summary>最近開過的檔案（最多 12 個），存在天幕島給文房的資料夾裡。</summary>
internal sealed class RecentFiles(string? directory)
{
    public const int Limit = 12;
    private readonly string? _file = directory is null ? null : Path.Combine(directory, "recent.json");

    public IReadOnlyList<string> Load()
    {
        try
        {
            if (_file is null || !File.Exists(_file)) return [];
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_file)) ?? [];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    public void Add(string path)
    {
        var list = Merge(Load(), path);
        Save(list);
    }

    public void Remove(string path) => Save(Load().Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToList());

    /// <summary>把剛開的檔案放到最前面、去掉重複、只留 12 個（純邏輯，方便測試）。</summary>
    public static List<string> Merge(IEnumerable<string> existing, string path) =>
        new[] { path }.Concat(existing.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase))).Take(Limit).ToList();

    private void Save(List<string> list)
    {
        try
        {
            if (_file is null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(list));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
