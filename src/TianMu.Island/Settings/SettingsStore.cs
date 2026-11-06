using System.Diagnostics;
using System.IO;
using System.Text.Json;
using TianMu.Core;

namespace TianMu.Island.Settings;

/// <summary>
/// 設定的讀寫中心。寫入採「先寫暫存檔再替換」，就算寫到一半斷電，舊設定也不會壞掉。
/// 所有成員皆可跨執行緒呼叫。
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _gate = new();
    private readonly string _path;
    private AppSettings _current;

    public SettingsStore(string? directory = null)
    {
        Directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TianMu-Island");
        _path = Path.Combine(Directory, "settings.json");
        _current = Load(_path);
    }

    /// <summary>設定檔所在資料夾。</summary>
    public string Directory { get; }

    /// <summary>設定變更後觸發（可能在任意執行緒）。</summary>
    public event Action<AppSettings>? Changed;

    /// <summary>取得目前設定的複本（修改它不會影響已存的設定，請用 <see cref="Update"/>）。</summary>
    public AppSettings Snapshot()
    {
        lock (_gate) return Clone(_current);
    }

    /// <summary>讀取單一設定值（不複製整份設定，適合頻繁呼叫）。</summary>
    public T Read<T>(Func<AppSettings, T> selector)
    {
        lock (_gate) return selector(_current);
    }

    /// <summary>修改並立即存檔。</summary>
    public void Update(Action<AppSettings> mutate)
    {
        AppSettings copy;
        lock (_gate)
        {
            mutate(_current);
            Save(_path, _current);
            copy = Clone(_current);
        }
        Changed?.Invoke(copy);
    }

    /// <summary>提供給法器讀取自己選項的介面。</summary>
    public IModuleSettings ForModule(string moduleId) => new ModuleView(this, moduleId);

    internal static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json);
                if (loaded is not null)
                {
                    loaded.Modules ??= [];
                    foreach (var m in loaded.Modules.Values)
                    {
                        m.Options ??= [];
                        m.Values ??= [];
                    }
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            // 設定檔損毀：保留一份備份，然後用預設值重新開始
            Debug.WriteLine($"[天幕] 設定檔讀取失敗，改用預設值：{ex.Message}");
            try { File.Copy(path, path + ".broken", overwrite: true); } catch { /* 備份失敗也無妨 */ }
        }
        return new AppSettings();
    }

    internal static void Save(string path, AppSettings settings)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[天幕] 設定檔寫入失敗：{ex.Message}");
        }
    }

    private static AppSettings Clone(AppSettings s) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s, Json), Json)!;

    private sealed class ModuleView(SettingsStore store, string moduleId) : IModuleSettings
    {
        public bool GetBool(string key, bool defaultValue)
        {
            lock (store._gate)
            {
                return store._current.Modules.TryGetValue(moduleId, out var m) &&
                       m.Options.TryGetValue(key, out bool value)
                    ? value
                    : defaultValue;
            }
        }

        public string GetString(string key, string defaultValue)
        {
            lock (store._gate)
            {
                return store._current.Modules.TryGetValue(moduleId, out var m) &&
                       m.Values.TryGetValue(key, out string? value) && value is not null
                    ? value
                    : defaultValue;
            }
        }
    }
}
