using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;

namespace TianMu.Modules.Music.Views;

/// <summary>音樂面板的資料（只能在 UI 執行緒修改）。</summary>
public sealed class MusicViewModel : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private ImageSource? _cover;
    private bool _isPlaying;
    private double _progress;
    private string _positionText = "0:00";
    private string _durationText = "0:00";
    private bool _hasTimeline;
    private string _currentLyric = string.Empty;
    private string _nextLyric = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get => _title; set => Set(ref _title, value); }
    public string Artist { get => _artist; set => Set(ref _artist, value); }
    public ImageSource? Cover { get => _cover; set { if (Set(ref _cover, value)) OnPropertyChanged(nameof(HasCover)); } }
    public bool HasCover => _cover is not null;
    public bool IsPlaying { get => _isPlaying; set { if (Set(ref _isPlaying, value)) OnPropertyChanged(nameof(PlayPauseGlyph)); } }
    public double Progress { get => _progress; set => Set(ref _progress, value); }
    public string PositionText { get => _positionText; set => Set(ref _positionText, value); }
    public string DurationText { get => _durationText; set => Set(ref _durationText, value); }
    public bool HasTimeline { get => _hasTimeline; set => Set(ref _hasTimeline, value); }
    public string CurrentLyric { get => _currentLyric; set => Set(ref _currentLyric, value); }
    public string NextLyric { get => _nextLyric; set => Set(ref _nextLyric, value); }

    /// <summary>Segoe MDL2 / Fluent Icons：E769 = 暫停、E768 = 播放。</summary>
    public string PlayPauseGlyph => _isPlaying ? "" : "";

    public ICommand PlayPauseCommand { get; init; } = RelayCommand.Empty;
    public ICommand NextCommand { get; init; } = RelayCommand.Empty;
    public ICommand PreviousCommand { get; init; } = RelayCommand.Empty;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>最簡單的 ICommand。</summary>
public sealed class RelayCommand(Action execute) : ICommand
{
    public static readonly RelayCommand Empty = new(() => { });

    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}
