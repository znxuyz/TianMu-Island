using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace TianMu.Modules.Sense.Watchers;

/// <summary>
/// 監看系統主音量與靜音（Core Audio）。切換預設輸出裝置（例如藍牙耳機連上）時自動改監看新裝置。
/// </summary>
internal sealed class VolumeWatcher : IDisposable
{
    private readonly object _gate = new();
    private MMDeviceEnumerator? _enumerator;
    private DeviceNotifier? _notifier;
    private MMDevice? _device;
    private bool _disposed;

    /// <summary>音量改變（0~1, 是否靜音），在背景執行緒觸發。</summary>
    public event Action<float, bool>? Changed;

    public void Start()
    {
        _ = Task.Run(() =>
        {
            lock (_gate)
            {
                try
                {
                    _enumerator = new MMDeviceEnumerator();
                    _notifier = new DeviceNotifier(() => Task.Run(Attach));
                    _enumerator.RegisterEndpointNotificationCallback(_notifier);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[靈識感應] 無法監看音量：{ex.Message}");
                    return;
                }
            }
            Attach();
        });
    }

    private void Attach()
    {
        lock (_gate)
        {
            if (_disposed || _enumerator is null) return;
            Detach();
            try
            {
                _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _device.AudioEndpointVolume.OnVolumeNotification += OnVolume;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[靈識感應] 找不到預設輸出裝置：{ex.Message}");
                _device = null;
            }
        }
    }

    private void Detach()
    {
        if (_device is null) return;
        try
        {
            _device.AudioEndpointVolume.OnVolumeNotification -= OnVolume;
            _device.Dispose();
        }
        catch { /* 裝置已移除 */ }
        _device = null;
    }

    private void OnVolume(AudioVolumeNotificationData data) => Changed?.Invoke(data.MasterVolume, data.Muted);

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Detach();
            if (_enumerator is not null && _notifier is not null)
            {
                try { _enumerator.UnregisterEndpointNotificationCallback(_notifier); } catch { }
            }
            _enumerator?.Dispose();
            _enumerator = null;
        }
    }
}

/// <summary>接收預設輸出裝置變更（COM 回呼，必須是 public 類別）。</summary>
public sealed class DeviceNotifier(Action onDefaultRenderChanged) : IMMNotificationClient
{
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia) onDefaultRenderChanged();
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
}
