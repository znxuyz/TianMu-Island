using System.Collections.Concurrent;

namespace TianMu.Modules.Notify.Notifications;

/// <summary>
/// 把通知來源的 App 識別碼（AUMID）轉成好讀的名稱。
/// 商店 App 向系統查詢正式名稱；傳統桌面程式用常見對照與字串整理。
/// </summary>
public static class AppNames
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly (string Contains, string Name)[] Known =
    [
        ("outlook", "Outlook"),
        ("windowscommunicationsapps", "郵件"),
        ("chrome", "Chrome"),
        ("msedge", "Edge"),
        ("firefox", "Firefox"),
        ("308046b0af4a39cb", "Firefox"),
        ("line", "LINE"),
        ("discord", "Discord"),
        ("teams", "Teams"),
        ("slack", "Slack"),
        ("telegram", "Telegram"),
        ("whatsapp", "WhatsApp"),
        ("yourphone", "手機連結"),
        ("phonelink", "手機連結"),
        ("spotify", "Spotify"),
        ("claude", "Claude"),
    ];

    public static string Resolve(string aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return "通知";
        return Cache.GetOrAdd(aumid, id => FromSystem(id) ?? Guess(id));
    }

    /// <summary>商店 App：向系統查詢顯示名稱（失敗回傳 null）。</summary>
    private static string? FromSystem(string aumid)
    {
        if (!aumid.Contains('!')) return null;
        try
        {
            string? name = Windows.ApplicationModel.AppInfo.GetFromAppUserModelId(aumid)?.DisplayInfo?.DisplayName;
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>傳統桌面程式：依常見對照，否則取最有意義的一段文字。</summary>
    public static string Guess(string aumid)
    {
        string lower = aumid.ToLowerInvariant();
        foreach (var (contains, name) in Known)
            if (lower.Contains(contains)) return name;

        // 「Microsoft.Office.OUTLOOK.EXE.15」→ 去掉 .EXE 與版本號、取最後一段
        string id = aumid.Split('!')[0];
        var parts = id.Split(['.', '_', '\\', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.Equals("exe", StringComparison.OrdinalIgnoreCase) && !p.All(char.IsDigit))
            .ToList();
        if (parts.Count == 0) return "通知";
        string last = parts[^1];
        return last.Length <= 2 && parts.Count > 1 ? parts[^2] : last;
    }
}
