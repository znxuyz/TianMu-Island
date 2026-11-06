using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Windows.Devices.Enumeration;

namespace TianMu.Modules.Sense.Watchers;

/// <summary>
/// 監看所有 USB 裝置的插拔（滑鼠、鍵盤、手機、耳機、讀卡機…），隨身碟則交給 <see cref="DriveWatcher"/> 顯示容量。
/// 啟動時已經插著的不算；同一個裝置常會同時出現好幾個介面，3 秒內同名的只算一次。
/// </summary>
internal sealed class UsbDeviceWatcher : IDisposable
{
    // GUID_DEVINTERFACE_USB_DEVICE
    private const string Selector = "System.Devices.InterfaceClassGuid:=\"{A5DCBF10-6530-11D2-901F-00C04FB951ED}\"";
    private static readonly TimeSpan SameDeviceWindow = TimeSpan.FromSeconds(3);

    private readonly ConcurrentDictionary<string, string> _names = new();
    private readonly ConcurrentDictionary<string, DateTime> _recent = new();
    private DeviceWatcher? _watcher;
    private volatile bool _ready;

    /// <summary>（裝置名稱, 是否接上），在背景執行緒觸發。</summary>
    public event Action<string, bool>? Changed;

    public void Start()
    {
        try
        {
            _watcher = DeviceInformation.CreateWatcher(Selector, null, DeviceInformationKind.DeviceInterface);
            _watcher.Added += (_, info) =>
            {
                string name = info.Name ?? string.Empty;
                _names[info.Id] = name;
                if (_ready) Report(name, plugged: true);
            };
            _watcher.Removed += (_, update) =>
            {
                if (_names.TryRemove(update.Id, out var name) && _ready) Report(name, plugged: false);
            };
            _watcher.EnumerationCompleted += (_, _) => _ready = true;
            _watcher.Start();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[靈識感應] 無法監看 USB 裝置：{ex.Message}");
        }
    }

    private void Report(string name, bool plugged)
    {
        if (!DeviceNames.IsWorthShowing(name)) return;

        string key = (plugged ? "+" : "-") + name;
        var now = DateTime.Now;
        if (_recent.TryGetValue(key, out var last) && now - last < SameDeviceWindow) return;
        _recent[key] = now;

        Changed?.Invoke(name, plugged);
    }

    public void Dispose()
    {
        try
        {
            if (_watcher?.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                _watcher.Stop();
        }
        catch { /* 收功時忽略 */ }
    }
}

/// <summary>裝置名稱的篩選（純邏輯，方便測試）。</summary>
public static partial class DeviceNames
{
    /// <summary>
    /// 名稱是否值得顯示：太短、像「A1B2C3」這種代碼、含有系統路徑符號的都不要；
    /// 集線器、根集線器、大容量存放裝置（隨身碟另有容量提示）也略過。
    /// </summary>
    public static bool IsWorthShowing(string? name)
    {
        if (!IsMeaningful(name)) return false;
        string n = name!;
        string[] skip = ["Hub", "集線器", "Root", "Mass Storage", "大容量", "Composite Device", "複合裝置"];
        return !skip.Any(s => n.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>名稱是否像人看得懂的裝置名稱（藍牙也用這個過濾）。</summary>
    public static bool IsMeaningful(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string n = name.Trim();
        if (n.Length < 3) return false;
        if (CodeRegex().IsMatch(n)) return false;
        return n.IndexOfAny(['\\', '{', '}']) < 0;
    }

    [GeneratedRegex("^[0-9A-F]{4,12}$|^([0-9A-F]{2}[:-]){5}[0-9A-F]{2}$", RegexOptions.IgnoreCase)]
    private static partial Regex CodeRegex();
}
