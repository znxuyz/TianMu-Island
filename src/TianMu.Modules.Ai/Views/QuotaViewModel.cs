using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace TianMu.Modules.Ai.Views;

public sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}

/// <summary>AI 額度卡片的資料（只在 UI 執行緒修改）。</summary>
public sealed class QuotaViewModel : INotifyPropertyChanged
{
    private bool _connected;
    private string _planText = string.Empty;
    private double _fiveHourRemaining;
    private string _fiveHourPercent = "—";
    private string _fiveHourReset = string.Empty;
    private double _sevenDayRemaining;
    private string _sevenDayPercent = "—";
    private string _sevenDayReset = string.Empty;
    private string _status = string.Empty;
    private string _emptyHint = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>是否已取得額度（false 時顯示「尚未連結」說明）。</summary>
    public bool Connected { get => _connected; set => Set(ref _connected, value); }

    /// <summary>方案名稱，例如「Max」。</summary>
    public string PlanText { get => _planText; set => Set(ref _planText, value); }

    public double FiveHourRemaining { get => _fiveHourRemaining; set => Set(ref _fiveHourRemaining, value); }
    public string FiveHourPercent { get => _fiveHourPercent; set => Set(ref _fiveHourPercent, value); }
    public string FiveHourReset { get => _fiveHourReset; set => Set(ref _fiveHourReset, value); }

    public double SevenDayRemaining { get => _sevenDayRemaining; set => Set(ref _sevenDayRemaining, value); }
    public string SevenDayPercent { get => _sevenDayPercent; set => Set(ref _sevenDayPercent, value); }
    public string SevenDayReset { get => _sevenDayReset; set => Set(ref _sevenDayReset, value); }

    /// <summary>底部狀態列：「更新於 14:20」或錯誤說明。</summary>
    public string Status { get => _status; set => Set(ref _status, value); }

    /// <summary>尚未連結時的說明文字。</summary>
    public string EmptyHint { get => _emptyHint; set => Set(ref _emptyHint, value); }

    public ICommand RefreshCommand { get; init; } = new RelayCommand(() => { });
    public ICommand ConnectCommand { get; init; } = new RelayCommand(() => { });

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
