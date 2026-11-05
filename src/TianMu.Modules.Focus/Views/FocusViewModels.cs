using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace TianMu.Modules.Focus.Views;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}

/// <summary>專注陣法的畫面資料（只在 UI 執行緒修改）。</summary>
public sealed class FocusViewModel : ViewModelBase
{
    private string _phaseLabel = "凝神";
    private string _statusLabel = "凝神 · 專注中";
    private string _timeText = "25:00";
    private double _progress;
    private string _roundText = "第一回";
    private bool _isBreak;
    private bool _isPaused;
    private string _realmTitle = "練氣 · 初期";
    private double _realmProgress;
    private string _realmHint = string.Empty;
    private string _todayText = string.Empty;

    /// <summary>收合時的兩字標籤：凝神 / 調息 / 暫歇。</summary>
    public string PhaseLabel { get => _phaseLabel; set => Set(ref _phaseLabel, value); }

    /// <summary>展開時的狀態列，例如「凝神 · 專注中」或「一回功成 · 修為 +25 分」。</summary>
    public string StatusLabel { get => _statusLabel; set => Set(ref _statusLabel, value); }

    public string TimeText { get => _timeText; set => Set(ref _timeText, value); }
    public double Progress { get => _progress; set => Set(ref _progress, value); }
    public string RoundText { get => _roundText; set => Set(ref _roundText, value); }
    public bool IsBreak { get => _isBreak; set => Set(ref _isBreak, value); }

    public bool IsPaused
    {
        get => _isPaused;
        set { if (Set(ref _isPaused, value)) Raise(nameof(PauseLabel)); }
    }

    public string PauseLabel => _isPaused ? "續功" : "暫歇";

    public string RealmTitle { get => _realmTitle; set => Set(ref _realmTitle, value); }
    public double RealmProgress { get => _realmProgress; set => Set(ref _realmProgress, value); }
    public string RealmHint { get => _realmHint; set => Set(ref _realmHint, value); }
    public string TodayText { get => _todayText; set => Set(ref _todayText, value); }

    public ICommand PauseResumeCommand { get; init; } = new RelayCommand(() => { });
    public ICommand AbandonCommand { get; init; } = new RelayCommand(() => { });
    public ICommand SkipBreakCommand { get; init; } = new RelayCommand(() => { });
}

/// <summary>突破境界時的畫面資料。</summary>
public sealed class BreakthroughViewModel : ViewModelBase
{
    private string _realmTitle = string.Empty;
    private string _detail = string.Empty;

    public string RealmTitle { get => _realmTitle; set => Set(ref _realmTitle, value); }
    public string Detail { get => _detail; set => Set(ref _detail, value); }
}

/// <summary>飲水提醒的畫面資料。</summary>
public sealed class WaterViewModel : ViewModelBase
{
    private string _detail = string.Empty;
    private IReadOnlyList<bool> _cups = [];

    public string Detail { get => _detail; set => Set(ref _detail, value); }

    /// <summary>今日每一盞是否已飲（長度 = 目標盞數）。</summary>
    public IReadOnlyList<bool> Cups { get => _cups; set => Set(ref _cups, value); }

    public ICommand DrinkCommand { get; init; } = new RelayCommand(() => { });
    public ICommand LaterCommand { get; init; } = new RelayCommand(() => { });
}

/// <summary>中文回數：第一回、第十二回。</summary>
public static class ChineseNumber
{
    private static readonly string[] Digits = ["〇", "一", "二", "三", "四", "五", "六", "七", "八", "九"];

    public static string Of(int n)
    {
        if (n <= 0) return Digits[0];
        if (n < 10) return Digits[n];
        if (n < 20) return "十" + (n % 10 == 0 ? "" : Digits[n % 10]);
        if (n < 100) return Digits[n / 10] + "十" + (n % 10 == 0 ? "" : Digits[n % 10]);
        return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
