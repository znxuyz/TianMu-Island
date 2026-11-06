using System.Diagnostics;
using System.Windows.Automation;

namespace TianMu.Modules.Sense.Watchers;

/// <summary>工作列上正在跑的一條進度。</summary>
/// <param name="App">哪個程式（例如 Google Chrome）。</param>
/// <param name="Fraction">0~1。</param>
public sealed record TaskbarProgress(string App, double Fraction);

/// <summary>
/// 從工作列讀出下載、複製、安裝等進度（透過 UI Automation，與螢幕報讀程式讀取的是同一份資料）。
/// 只讀取、不操作；每 1.5 秒看一次，在背景執行緒進行，不會卡住畫面。
/// </summary>
public sealed class TaskbarProgressWatcher : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(1500);

    private readonly Timer _timer;
    private AutomationElement? _taskbar;
    private TaskbarProgress? _last;
    private int _scanning;
    private bool _disposed;

    /// <summary>進度出現、改變或消失（null）時觸發（背景執行緒）。</summary>
    public event Action<TaskbarProgress?>? Changed;

    public TaskbarProgressWatcher() => _timer = new Timer(_ => Scan(), null, Timeout.Infinite, Timeout.Infinite);

    public void Start() => _timer.Change(TimeSpan.FromSeconds(2), Interval);

    private void Scan()
    {
        if (_disposed || Interlocked.Exchange(ref _scanning, 1) == 1) return;
        try
        {
            _taskbar ??= FindTaskbar();
            if (_taskbar is null) return;

            var readings = new List<(string Name, double Value, double Max)>();
            var condition = new PropertyCondition(AutomationElement.IsRangeValuePatternAvailableProperty, true);
            foreach (AutomationElement element in _taskbar.FindAll(TreeScope.Descendants, condition))
            {
                try
                {
                    if (element.TryGetCurrentPattern(RangeValuePattern.Pattern, out object pattern) && pattern is RangeValuePattern range)
                        readings.Add((element.Current.Name, range.Current.Value, range.Current.Maximum));
                }
                catch { /* 單一按鈕剛好消失，略過 */ }
            }

            Report(TaskbarProgressReading.Pick(readings));
        }
        catch (Exception ex)
        {
            // 檔案總管重新啟動時工作列會換一個，下次重新找
            Debug.WriteLine($"[工作列進度] 讀取失敗：{ex.Message}");
            _taskbar = null;
        }
        finally
        {
            Interlocked.Exchange(ref _scanning, 0);
        }
    }

    private void Report(TaskbarProgress? now)
    {
        // 百分比只在整數改變時才通知，免得畫面一直重繪
        bool same = now is null ? _last is null
            : _last is not null && _last.App == now.App && (int)(_last.Fraction * 100) == (int)(now.Fraction * 100);
        if (same) return;
        _last = now;
        Changed?.Invoke(now);
    }

    private static AutomationElement? FindTaskbar()
    {
        var bar = AutomationElement.RootElement.FindFirst(TreeScope.Children,
            new PropertyCondition(AutomationElement.ClassNameProperty, "Shell_TrayWnd"));
        if (bar is null) return null;

        // Windows 10 的按鈕列表；找不到（Windows 11）就整條工作列一起找
        return bar.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ClassNameProperty, "MSTaskListWClass")) ?? bar;
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}

/// <summary>從工作列上的進度條讀數挑出要顯示的那一條（純邏輯，方便測試）。</summary>
public static class TaskbarProgressReading
{
    /// <summary>
    /// 挑出進行中的進度（0% 與 100% 視為沒有進度：剛開始的空條、完成後還留著的綠條）。
    /// 有多條時顯示最接近完成的那一條。
    /// </summary>
    public static TaskbarProgress? Pick(IEnumerable<(string Name, double Value, double Max)> readings)
    {
        TaskbarProgress? best = null;
        foreach (var (name, value, max) in readings)
        {
            if (max <= 0 || double.IsNaN(value) || double.IsNaN(max)) continue;
            double fraction = value / max;
            if (fraction <= 0.001 || fraction >= 0.999) continue;
            if (best is null || fraction > best.Fraction)
                best = new TaskbarProgress(CleanName(name), fraction);
        }
        return best;
    }

    /// <summary>
    /// 工作列按鈕的名稱像「Google Chrome - 1 個執行中的視窗」或「檔案總管 - 2 running windows」，只留程式名稱。
    /// </summary>
    public static string CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "工作列";
        string s = name.Trim();
        int dash = s.LastIndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0) s = s[..dash].Trim();
        return s.Length > 18 ? s[..17] + "…" : s;
    }
}
