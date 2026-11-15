namespace TianMu.Modules.Notify.Notifications;

/// <summary>通知從天幕跳出來時要顯示多少內容。</summary>
public enum NoticeDisplay
{
    /// <summary>完整：標題與內容（例如「阿哲：晚上七點吃飯？」）。</summary>
    Full,
    /// <summary>只顯示誰：「阿哲 傳來一則訊息」，看不到內容。</summary>
    Sender,
    /// <summary>只顯示 App：「LINE 有一則新訊息」，連誰傳的都不顯示。</summary>
    App,
}

/// <summary>
/// 依顯示方式產生天幕上的兩行字（純邏輯，方便測試）。
/// 聊天、郵件這類 App 的通知標題通常就是傳訊的人，所以「只顯示誰」時用標題當名字；
/// 其他 App 的標題可能就是內容（例如「下載完成：報價單.pdf」），一律只說是哪個 App。
/// </summary>
public static class NoticeFormat
{
    public const string FullValue = "full";
    public const string SenderValue = "sender";
    public const string AppValue = "app";

    /// <summary>會「有人傳訊息給你」的 App（以名稱或網站比對，不分大小寫）。</summary>
    private static readonly string[] Messaging =
    [
        "line", "messenger", "whatsapp", "telegram", "discord", "teams", "slack", "wechat", "微信", "signal", "skype",
        "kakao", "instagram", "facebook", "手機連結", "phone link", "outlook", "郵件", "mail", "gmail", "簡訊", "訊息",
    ];

    public static NoticeDisplay Parse(string? value) => value switch
    {
        SenderValue => NoticeDisplay.Sender,
        AppValue => NoticeDisplay.App,
        _ => NoticeDisplay.Full,
    };

    public static bool IsMessaging(string source) =>
        Messaging.Any(m => source.Contains(m, StringComparison.OrdinalIgnoreCase));

    /// <summary>天幕上顯示的標題與第二行。</summary>
    public static (string Title, string Body) Format(NoticeDisplay mode, string title, string body, string source)
    {
        title = title.Trim();
        bool chat = IsMessaging(source);
        switch (mode)
        {
            case NoticeDisplay.Sender when chat && title.Length > 0:
                return ($"{title} 傳來一則訊息", "內容已隱藏，點開「傳音」頁再看");
            case NoticeDisplay.Sender:
            case NoticeDisplay.App:
                return ($"{source} {(chat ? "有一則新訊息" : "有一則新通知")}", "內容已隱藏，點開「傳音」頁再看");
            default:
                return (title.Length > 0 ? title : source, body);
        }
    }
}
