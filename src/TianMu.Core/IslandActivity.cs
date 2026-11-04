namespace TianMu.Core;

/// <summary>異象的優先級：數字越大越能搶佔天幕。</summary>
public enum ActivityPriority
{
    Ambient = 0,      // 常駐：時鐘、播放中的音樂
    Normal = 10,      // 一般：番茄鐘階段切換
    Alert = 20,       // 提醒：系統通知、USB 插拔、藍牙連線
    Critical = 30,    // 緊急：低電量等
}

/// <summary>
/// 一則要顯化在天幕上的「異象」。第一階段只有文字；後續會擴充封面、頻譜、自訂面板等。
/// </summary>
/// <param name="ModuleId">發出異象的法器 Id。</param>
/// <param name="Title">主標題。</param>
/// <param name="Subtitle">副標題（可選）。</param>
/// <param name="Priority">優先級。</param>
/// <param name="Duration">顯化多久後自動收起；null 代表常駐直到 Dismiss。</param>
public sealed record IslandActivity(
    string ModuleId,
    string Title,
    string? Subtitle = null,
    ActivityPriority Priority = ActivityPriority.Normal,
    TimeSpan? Duration = null);
