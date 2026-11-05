namespace TianMu.Core;

/// <summary>異象的優先級：數字越大越能搶佔天幕。</summary>
public enum ActivityPriority
{
    /// <summary>常駐：收合時顯示在膠囊裡（例如播放中的音樂），不會自動展開。</summary>
    Ambient = 0,
    /// <summary>一般：會自動展開一段時間（例如換歌、番茄鐘階段切換）。</summary>
    Normal = 10,
    /// <summary>提醒：系統通知、USB 插拔、藍牙連線。</summary>
    Alert = 20,
    /// <summary>緊急：低電量等。</summary>
    Critical = 30,
}

/// <summary>
/// 一則要顯化在天幕上的「異象」。
/// 最簡單的用法只給文字；需要自訂畫面的法器可以提供 <see cref="CompactView"/> / <see cref="ExpandedView"/>。
/// </summary>
/// <param name="ModuleId">發出異象的法器 Id。</param>
/// <param name="Title">主標題（沒有自訂畫面時顯示）。</param>
/// <param name="Subtitle">副標題（可選）。</param>
/// <param name="Priority">優先級。</param>
/// <param name="Duration">顯化多久後自動收起；null 代表常駐直到 Dismiss。</param>
public sealed record IslandActivity(
    string ModuleId,
    string Title,
    string? Subtitle = null,
    ActivityPriority Priority = ActivityPriority.Normal,
    TimeSpan? Duration = null)
{
    /// <summary>
    /// 收合時膠囊內的自訂畫面（WPF <c>FrameworkElement</c>，必須在 UI 執行緒建立）。
    /// 以 object 宣告是為了讓 Core 不依賴 WPF。
    /// </summary>
    public object? CompactView { get; init; }

    /// <summary>
    /// 分身小圓的畫面（WPF <c>FrameworkElement</c>，約 22×22）。
    /// 同時有兩個常駐異象時（例如音樂＋番茄鐘），天幕像水滴一樣分出一顆小圓顯示較早的那一個。
    /// </summary>
    public object? BadgeView { get; init; }

    /// <summary>展開時大面板的自訂畫面（WPF <c>FrameworkElement</c>）。</summary>
    public object? ExpandedView { get; init; }

    /// <summary>收合時膠囊寬度（DIP）。null 用宿主預設。</summary>
    public double? CompactWidth { get; init; }

    /// <summary>展開時面板寬度（DIP）。null 用宿主預設。</summary>
    public double? ExpandedWidth { get; init; }

    /// <summary>展開時面板高度（DIP）。null 用宿主預設。</summary>
    public double? ExpandedHeight { get; init; }
}
