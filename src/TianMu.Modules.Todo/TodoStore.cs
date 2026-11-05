using System.IO;
using System.Text.Json;

namespace TianMu.Modules.Todo;

/// <summary>一筆待辦。</summary>
public sealed class TodoItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Text { get; set; } = string.Empty;

    /// <summary>何時提醒（本地時間）。稍後提醒會把它往後挪。</summary>
    public DateTime DueAt { get; set; }

    /// <summary>這個時間點是否已經提醒過（避免重複跳出）。</summary>
    public bool Notified { get; set; }

    public bool Done { get; set; }
    public DateTime? DoneAt { get; set; }
}

/// <summary>
/// 天機簿的清單：依提醒時間排序，完成的保留一天後自動清掉。
/// </summary>
public sealed class TodoStore
{
    public const int Capacity = 100;
    private static readonly TimeSpan KeepDoneFor = TimeSpan.FromDays(1);

    private readonly List<TodoItem> _items = [];

    public IReadOnlyList<TodoItem> Items => _items;

    /// <summary>尚未完成的待辦，依提醒時間由近到遠。</summary>
    public IReadOnlyList<TodoItem> Pending => _items.Where(i => !i.Done).OrderBy(i => i.DueAt).ToList();

    public TodoItem Add(string text, DateTime dueAt)
    {
        var item = new TodoItem { Text = text.Trim(), DueAt = dueAt };
        _items.Add(item);
        if (_items.Count > Capacity)
        {
            // 太多時先丟掉最舊的已完成項目，再丟最舊的待辦
            var oldest = _items.Where(i => i.Done).OrderBy(i => i.DoneAt).FirstOrDefault()
                         ?? _items.OrderBy(i => i.DueAt).First();
            _items.Remove(oldest);
        }
        return item;
    }

    public TodoItem? Find(Guid id) => _items.FirstOrDefault(i => i.Id == id);

    public bool Complete(Guid id, DateTime now)
    {
        if (Find(id) is not { Done: false } item) return false;
        item.Done = true;
        item.DoneAt = now;
        return true;
    }

    public bool Remove(Guid id) => _items.RemoveAll(i => i.Id == id) > 0;

    /// <summary>稍後再提醒：從現在起往後挪。</summary>
    public bool Snooze(Guid id, TimeSpan delay, DateTime now)
    {
        if (Find(id) is not { Done: false } item) return false;
        item.DueAt = now + delay;
        item.Notified = false;
        return true;
    }

    /// <summary>已經到點、還沒提醒過的待辦（最早的在前）。</summary>
    public IReadOnlyList<TodoItem> Due(DateTime now) =>
        _items.Where(i => !i.Done && !i.Notified && i.DueAt <= now).OrderBy(i => i.DueAt).ToList();

    /// <summary>清掉完成超過一天的項目。回傳清掉的數量。</summary>
    public int Prune(DateTime now) => _items.RemoveAll(i => i.Done && i.DoneAt is { } at && now - at > KeepDoneFor);

    // ── 存檔 ──────────────────────────────────

    public static TodoStore Load(string file)
    {
        var store = new TodoStore();
        try
        {
            if (File.Exists(file) && JsonSerializer.Deserialize<List<TodoItem>>(File.ReadAllText(file)) is { } list)
                store._items.AddRange(list.Where(i => !string.IsNullOrWhiteSpace(i.Text)).Take(Capacity));
        }
        catch { /* 損毀就從空的開始 */ }
        return store;
    }

    public void Save(string file)
    {
        string temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_items));
        File.Move(temp, file, overwrite: true);
    }
}
