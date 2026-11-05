using System.Diagnostics;
using Microsoft.Win32;

namespace TianMu.Modules.Sense.Watchers;

/// <summary>一個正在使用麥克風或相機的程式。</summary>
public sealed record CapabilityUser(string AppName);

/// <summary>判斷「使用中」的純邏輯（方便測試）。</summary>
public static class CapabilityUsage
{
    /// <summary>Windows 的紀錄：開始時間有值、結束時間為 0，代表此刻正在使用。</summary>
    public static bool IsInUse(long lastUsedStart, long lastUsedStop) => lastUsedStart > 0 && lastUsedStop == 0;

    /// <summary>
    /// 把登錄檔裡的程式識別轉成好讀的名稱：
    ///   傳統程式「C:#Program Files#Zoom#bin#Zoom.exe」→「Zoom」
    ///   商店 App「Microsoft.WindowsCamera_8wekyb3d8bbwe」→「WindowsCamera」
    /// </summary>
    public static string AppName(string key)
    {
        if (key.Contains('#'))
        {
            string file = key.Split('#', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? key;
            return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
        }

        string id = key.Split('_')[0];
        string[] parts = id.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[^1] : key;
    }
}

/// <summary>
/// 每 2 秒查看 Windows 的「隱私權 → 麥克風／相機」使用紀錄
/// （HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore），
/// 回報目前正在使用的程式。只讀登錄檔，不需要任何權限。
/// </summary>
internal sealed class PrivacyWatcher : IDisposable
{
    private const string ConsentStore = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly Timer _timer;
    private string _lastMic = string.Empty, _lastCam = string.Empty;

    public PrivacyWatcher()
    {
        _timer = new Timer(_ => Poll(), null, TimeSpan.FromSeconds(1), Interval);
    }

    /// <summary>使用者清單改變（麥克風使用者, 相機使用者），在背景執行緒觸發。</summary>
    public event Action<IReadOnlyList<CapabilityUser>, IReadOnlyList<CapabilityUser>>? Changed;

    private void Poll()
    {
        try
        {
            var mic = Users("microphone");
            var cam = Users("webcam");
            string micKey = string.Join("|", mic.Select(u => u.AppName));
            string camKey = string.Join("|", cam.Select(u => u.AppName));
            if (micKey == _lastMic && camKey == _lastCam) return;

            _lastMic = micKey;
            _lastCam = camKey;
            Changed?.Invoke(mic, cam);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[靈識感應] 讀取麥克風／相機狀態失敗：{ex.Message}");
        }
    }

    private static List<CapabilityUser> Users(string capability)
    {
        var users = new List<CapabilityUser>();
        using var root = Registry.CurrentUser.OpenSubKey($@"{ConsentStore}\{capability}");
        if (root is null) return users;

        // 商店 App 直接在底下；傳統程式在 NonPackaged 子機碼裡
        Collect(root, users, skip: "NonPackaged");
        using var nonPackaged = root.OpenSubKey("NonPackaged");
        if (nonPackaged is not null) Collect(nonPackaged, users, skip: null);

        return users.DistinctBy(u => u.AppName).ToList();
    }

    private static void Collect(RegistryKey parent, List<CapabilityUser> users, string? skip)
    {
        foreach (string name in parent.GetSubKeyNames())
        {
            if (name == skip) continue;
            using var key = parent.OpenSubKey(name);
            if (key is null) continue;
            long start = key.GetValue("LastUsedTimeStart") is long s ? s : 0;
            long stop = key.GetValue("LastUsedTimeStop") is long t ? t : 0;
            if (CapabilityUsage.IsInUse(start, stop)) users.Add(new CapabilityUser(CapabilityUsage.AppName(name)));
        }
    }

    public void Dispose() => _timer.Dispose();
}
