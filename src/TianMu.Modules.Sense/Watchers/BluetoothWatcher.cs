using System.Collections.Concurrent;
using System.Diagnostics;
using Windows.Devices.Enumeration;

namespace TianMu.Modules.Sense.Watchers;

/// <summary>
/// 監看「已配對」藍牙裝置（傳統藍牙與低功耗藍牙）的連線狀態，例如耳機連上或斷線。
/// 啟動時的初始狀態不會觸發通知；同一裝置 5 秒內的反覆切換會被忽略（有些裝置會短暫重連）。
/// </summary>
internal sealed class BluetoothWatcher : IDisposable
{
    private const string IsConnectedKey = "System.Devices.Aep.IsConnected";
    private const string ClassicProtocol = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";
    private const string LowEnergyProtocol = "{bb7bb05e-5972-42b5-94fc-76eaa7084d49}";
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(5);

    private readonly List<DeviceWatcher> _watchers = [];
    private readonly ConcurrentDictionary<string, (string Name, bool Connected, DateTime ChangedAt)> _devices = new();

    /// <summary>連線狀態改變（裝置名稱, 是否已連線），在背景執行緒觸發。</summary>
    public event Action<string, bool>? ConnectionChanged;

    public void Start()
    {
        foreach (string protocol in new[] { ClassicProtocol, LowEnergyProtocol })
        {
            try
            {
                string aqs = $"System.Devices.Aep.ProtocolId:=\"{protocol}\" AND System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#True";
                var watcher = DeviceInformation.CreateWatcher(aqs, [IsConnectedKey], DeviceInformationKind.AssociationEndpoint);
                watcher.Added += OnAdded;
                watcher.Updated += OnUpdated;
                watcher.Start();
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                // 沒有藍牙介面卡的電腦會走到這裡
                Debug.WriteLine($"[靈識感應] 無法監看藍牙：{ex.Message}");
            }
        }
    }

    private void OnAdded(DeviceWatcher sender, DeviceInformation info)
    {
        bool connected = info.Properties.TryGetValue(IsConnectedKey, out var value) && value is true;
        _devices[info.Id] = (string.IsNullOrWhiteSpace(info.Name) ? "藍牙裝置" : info.Name, connected, DateTime.MinValue);
    }

    private void OnUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        if (!update.Properties.TryGetValue(IsConnectedKey, out var value) || value is not bool connected) return;
        if (!_devices.TryGetValue(update.Id, out var known) || known.Connected == connected) return;

        var now = DateTime.Now;
        bool flapping = now - known.ChangedAt < Debounce;
        _devices[update.Id] = (known.Name, connected, now);
        if (!flapping && DeviceNames.IsMeaningful(known.Name)) ConnectionChanged?.Invoke(known.Name, connected);
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Added -= OnAdded;
            watcher.Updated -= OnUpdated;
            try
            {
                if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                    watcher.Stop();
            }
            catch { /* 收功時忽略 */ }
        }
        _watchers.Clear();
    }
}
