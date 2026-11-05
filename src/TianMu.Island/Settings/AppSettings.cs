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
}
