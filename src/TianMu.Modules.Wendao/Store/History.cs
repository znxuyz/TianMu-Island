using System.IO;
using System.Text.Json;
using TianMu.Modules.Wendao.Ai;

namespace TianMu.Modules.Wendao.Store;

/// <summary>一則存檔的訊息（圖片不存，只記有附圖）。</summary>
public sealed class SavedMessage
{
    public Role Role { get; set; }
    public string Text { get; set; } = "";
    public string? Model { get; set; }
    public bool HadImage { get; set; }
}

public sealed class Conversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "新的對話";
    public DateTime Updated { get; set; } = DateTime.Now;
    public List<SavedMessage> Messages { get; set; } = [];

    /// <summary>用第一個問題當標題（最多 24 字）。</summary>
    public static string TitleFrom(string question)
    {
        string t = string.Join(' ', question.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (t.Length == 0) return "新的對話";
        return t.Length > 24 ? t[..24] + "…" : t;
    }
}

/// <summary>對話紀錄：每段對話一個 JSON 檔，存在天幕島給問道的資料夾（只在你的電腦上）。</summary>
internal sealed class History(string? directory)
{
    private readonly string? _dir = directory is null ? null : Path.Combine(directory, "conversations");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public List<Conversation> LoadAll()
    {
        if (_dir is null || !Directory.Exists(_dir)) return [];
        var list = new List<Conversation>();
        foreach (var f in Directory.EnumerateFiles(_dir, "*.json"))
        {
            try { if (JsonSerializer.Deserialize<Conversation>(File.ReadAllText(f), Json) is { } c) list.Add(c); }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return list.OrderByDescending(c => c.Updated).ToList();
    }

    public void Save(Conversation c)
    {
        if (_dir is null || c.Messages.Count == 0) return;
        try
        {
            Directory.CreateDirectory(_dir);
            string path = Path.Combine(_dir, c.Id + ".json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(c, Json));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public void Delete(Conversation c)
    {
        if (_dir is null) return;
        try { File.Delete(Path.Combine(_dir, c.Id + ".json")); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
