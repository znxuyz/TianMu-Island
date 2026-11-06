using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using TianMu.Modules.Sense.Watchers;

namespace TianMu.Modules.Sense.Views;

/// <summary>周天運轉的畫面資料（只在 UI 執行緒修改）。</summary>
public sealed class SystemViewModel : INotifyPropertyChanged
{
    private SystemStats _stats = new(0, 0, 0, 0);

    public event PropertyChangedEventHandler? PropertyChanged;

    public double Cpu => _stats.Cpu;
    public double Memory => _stats.Memory;
    public string CpuText => Percent(_stats.Cpu);
    public string MemoryText => Percent(_stats.Memory);
    public string DownText => SystemMath.SpeedText(_stats.DownBytesPerSecond);
    public string UpText => SystemMath.SpeedText(_stats.UpBytesPerSecond);

    /// <summary>收合時的一行：「CPU 23% · 記憶體 61%」。</summary>
    public string CompactText => $"CPU {CpuText} · 記憶體 {MemoryText}";

    public void Update(SystemStats stats)
    {
        _stats = stats;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty)); // 全部重新讀取
    }

    private static string Percent(double v) => ((int)Math.Round(v * 100)).ToString(CultureInfo.InvariantCulture) + "%";
}
