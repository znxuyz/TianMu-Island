using System.Windows.Media.Imaging;

namespace TianMu.Modules.Office;

/// <summary>剪貼簿裡的一筆內容。</summary>
public enum ClipKind { Text, Image, Files }

/// <summary>萬象簿的一筆紀錄。</summary>
public sealed class ClipEntry
{
    public Guid Id { get; } = Guid.NewGuid();
    public required ClipKind Kind { get; init; }
    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<string> Files { get; init; } = [];
    public BitmapSource? Image { get; init; }

    /// <summary>清單上的小縮圖（圖片才有）。</summary>
    public BitmapSource? Thumb { get; init; }
    public DateTime At { get; init; } = DateTime.Now;
    public bool Pinned { get; set; }

    /// <summary>判斷是不是同一份內容（重複複製只留一筆，移到最前面）。</summary>
    public string Key => Kind switch
    {
        ClipKind.Text => "t:" + Text,
        ClipKind.Files => "f:" + string.Join("|", Files),
        _ => "i:" + (Image is null ? "" : $"{Image.PixelWidth}x{Image.PixelHeight}:{ImageHash}"),
    };

    public string ImageHash { get; init; } = string.Empty;
}

/// <summary>
/// 萬象簿的清單（純邏輯，方便測試）：最新的在最前面，最多 <see cref="Capacity"/> 筆；
/// 釘選的不會被擠掉；同一份內容再複製一次只會移到最前面。
/// </summary>
public sealed class ClipHistory
{
    public const int Capacity = 10;
    public const int MaxTextLength = 20_000;

    private readonly List<ClipEntry> _items = [];

    public IReadOnlyList<ClipEntry> Items => _items;

    /// <summary>加入一筆。回傳是否真的有變動。</summary>
    public bool Add(ClipEntry entry)
    {
        if (entry.Kind == ClipKind.Text && string.IsNullOrWhiteSpace(entry.Text)) return false;
        if (entry.Kind == ClipKind.Files && entry.Files.Count == 0) return false;

        int existing = _items.FindIndex(i => i.Key == entry.Key);
        if (existing == 0) return false; // 已經是最新的一筆
        bool pinned = false;
        if (existing > 0)
        {
            pinned = _items[existing].Pinned;
            _items.RemoveAt(existing);
        }
        entry.Pinned = pinned;
        _items.Insert(0, entry);

        // 超過上限：從最舊的、沒釘選的開始拿掉
        while (_items.Count > Capacity)
        {
            int victim = _items.FindLastIndex(i => !i.Pinned);
            if (victim < 0) break; // 全部都釘選了
            _items.RemoveAt(victim);
        }
        return true;
    }

    public void TogglePin(Guid id)
    {
        if (_items.Find(i => i.Id == id) is { } item) item.Pinned = !item.Pinned;
    }

    public bool Remove(Guid id) => _items.RemoveAll(i => i.Id == id) > 0;

    /// <summary>清空（釘選的保留）。</summary>
    public void ClearUnpinned() => _items.RemoveAll(i => !i.Pinned);

    /// <summary>清單上顯示的一行預覽：去掉多餘空白，太長就截斷。</summary>
    public static string Preview(string text, int max = 60)
    {
        var sb = new System.Text.StringBuilder();
        bool space = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!space && sb.Length > 0) sb.Append(' ');
                space = true;
            }
            else
            {
                sb.Append(c);
                space = false;
            }
            if (sb.Length > max) break;
        }
        string s = sb.ToString().TrimEnd();
        return s.Length > max ? s[..max] + "…" : s;
    }
}
