using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace TianMu.Modules.Sense.Views;

/// <summary>感應提示的畫面資料：圖示、標題、說明，以及可選的刻度條（音量、亮度）。</summary>
public sealed class SignalViewModel : INotifyPropertyChanged
{
    private string _glyph = "";
    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private bool _hasLevel;
    private double _level;
    private string _levelText = string.Empty;
    private Brush? _accent;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Segoe Fluent Icons / MDL2 Assets 的字元。</summary>
    public string Glyph { get => _glyph; set => Set(ref _glyph, value); }
    public string Title { get => _title; set => Set(ref _title, value); }
    public string Subtitle { get => _subtitle; set => Set(ref _subtitle, value); }
    public bool HasLevel { get => _hasLevel; set => Set(ref _hasLevel, value); }

    /// <summary>刻度（0~1）。</summary>
    public double Level { get => _level; set => Set(ref _level, value); }
    public string LevelText { get => _levelText; set => Set(ref _levelText, value); }

    /// <summary>圖示與刻度的顏色（器有其色）。</summary>
    public Brush? Accent { get => _accent; set => Set(ref _accent, value); }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
