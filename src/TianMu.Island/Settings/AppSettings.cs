using System.Text.Json.Serialization;
using TianMu.Island.Shell;

namespace TianMu.Island.Settings;

/// <summary>
/// 天幕島的所有使用者設定。存放於 %APPDATA%\TianMu-Island\settings.json。
/// 新增欄位時一律給預設值，舊版設定檔讀進來才不會出錯。
/// </summary>
public sealed class AppSettings
{
    /// <summary>設定檔格式版本，未來格式變動時用來做轉換。</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>開機時自動啟動天幕島。</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>天幕停靠位置（拖曳天幕即可改變）。</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<DockPosition>))]
    public DockPosition Dock { get; set; } = DockPosition.TopCenter;

    /// <summary>停靠在哪一個螢幕（裝置名稱，例如 \\.\DISPLAY2）；找不到時用主螢幕。</summary>
    public string? DockScreen { get; set; }

    /// <summary>前景程式全螢幕時（影片、遊戲、簡報）自動隱藏天幕。</summary>
    public bool AutoHideFullscreen { get; set; } = true;

    /// <summary>滑鼠靠近時，天幕微微朝游標偏移。</summary>
    public bool Magnetic { get; set; } = true;

    /// <summary>多螢幕時，天幕跟著滑鼠移到所在的螢幕。</summary>
    public bool FollowCursorScreen { get; set; } = true;

    /// <summary>漆面色溫隨時辰流轉。</summary>
    public bool DayNightTint { get; set; } = true;

    /// <summary>智慧隱藏：閒置時天幕縮成一條細線，滑鼠碰到螢幕邊緣或移到它上面才變回來。</summary>
    public bool SmartHide { get; set; }

    /// <summary>彈簧手感：「靈動」晃動較多、「俐落」較快停下。</summary>
    public string SpringFeel { get; set; } = SpringFeels.Lively;

    /// <summary>番茄鐘專注時，螢幕邊緣亮起一圈緩緩呼吸的金色結界。</summary>
    public bool FocusBarrier { get; set; } = true;

    /// <summary>閒置時，膠囊兩端飄散靈氣粒子。</summary>
    public bool ShowParticles { get; set; } = true;

    /// <summary>自動檢查新版本（每天最多一次）。</summary>
    public bool AutoCheckUpdates { get; set; } = true;

    /// <summary>上次檢查更新的時間。</summary>
    public DateTimeOffset? LastUpdateCheck { get; set; }

    /// <summary>使用者選擇略過的版本（不再提醒）。</summary>
    public string? SkippedVersion { get; set; }

    /// <summary>各法器的開關與選項，以法器 Id 為鍵。</summary>
    public Dictionary<string, ModuleSettings> Modules { get; set; } = [];

    public ModuleSettings GetModule(string moduleId)
    {
        if (!Modules.TryGetValue(moduleId, out var module))
        {
            module = new ModuleSettings();
            Modules[moduleId] = module;
        }
        return module;
    }

    public bool IsModuleEnabled(string moduleId) =>
        !Modules.TryGetValue(moduleId, out var module) || module.Enabled;
}

/// <summary>單一法器的設定。</summary>
public sealed class ModuleSettings
{
    public bool Enabled { get; set; } = true;

    public Dictionary<string, bool> Options { get; set; } = [];

    /// <summary>選擇題與文字選項。</summary>
    public Dictionary<string, string> Values { get; set; } = [];
}

/// <summary>彈簧手感的選項。</summary>
public static class SpringFeels
{
    public const string Lively = "lively";
    public const string Crisp = "crisp";
}
