using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace TianMu.Modules.Sense.Views;

/// <summary>煉化進度的畫面資料（只在 UI 執行緒修改）。</summary>
public sealed class ProgressViewModel : INotifyPropertyChanged
{
    private string _app = string.Empty;
    private double _progress;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>哪個程式（例如 Google Chrome）。</summary>
    public string App { get => _app; set { if (_app == value) return; _app = value; Raise(); } }

    /// <summary>0~1。</summary>
    public double Progress
    {
        get => _progress;
        set
        {
            if (_progress.Equals(value)) return;
            _progress = value;
            Raise();
            Raise(nameof(PercentText));
        }
    }

    public string PercentText => ((int)(_progress * 100)).ToString(CultureInfo.InvariantCulture) + "%";

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
