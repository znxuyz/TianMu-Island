using System.Diagnostics;
using System.Management;

namespace TianMu.Modules.Sense.Watchers;

/// <summary>
/// 監看內建螢幕亮度（WMI WmiMonitorBrightnessEvent）。
/// 只有筆電、平板等內建螢幕支援；桌上型電腦的外接螢幕會安靜地不啟用。
/// </summary>
internal sealed class BrightnessWatcher : IDisposable
{
    private ManagementEventWatcher? _watcher;

    /// <summary>亮度改變（0~100），在背景執行緒觸發。</summary>
    public event Action<int>? Changed;

    public bool IsSupported { get; private set; }

    public void Start()
    {
        _ = Task.Run(() =>
        {
            try
            {
                var query = new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent");
                _watcher = new ManagementEventWatcher(new ManagementScope(@"root\WMI"), query);
                _watcher.EventArrived += OnEvent;
                _watcher.Start();
                IsSupported = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[靈識感應] 此電腦不支援亮度通知：{ex.Message}");
                _watcher?.Dispose();
                _watcher = null;
            }
        });
    }

    private void OnEvent(object sender, EventArrivedEventArgs e)
    {
        try
        {
            if (e.NewEvent["Brightness"] is byte level) Changed?.Invoke(level);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[靈識感應] 讀取亮度失敗：{ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_watcher is null) return;
        try
        {
            _watcher.EventArrived -= OnEvent;
            _watcher.Stop();
        }
        catch { /* 收功時忽略 */ }
        _watcher.Dispose();
        _watcher = null;
    }
}
