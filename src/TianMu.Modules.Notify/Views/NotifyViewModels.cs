using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace TianMu.Modules.Notify.Views;

public sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>一則通知（符紙）。</summary>
public sealed class NoticeViewModel : ViewModelBase
{
    private string _title = string.Empty;
    private string _body = string.Empty;
    private string _source = string.Empty;

    public string Title { get => _title; set => Set(ref _title, value); }
    public string Body { get => _body; set => Set(ref _body, value); }

    /// <summary>「LINE · 剛剛」。</summary>
    public string Source { get => _source; set => Set(ref _source, value); }
}

/// <summary>驗證碼卡片。</summary>
public sealed class OtpViewModel : ViewModelBase
{
    private string _code = string.Empty;
    private string _prettyCode = string.Empty;
    private string _source = string.Empty;
    private string _action = "點擊複製";
    private bool _hasCode;

    public string Code { get => _code; set => Set(ref _code, value); }

    /// <summary>分段顯示：「482 913」。</summary>
    public string PrettyCode { get => _prettyCode; set => Set(ref _prettyCode, value); }

    /// <summary>「GitHub · 09:12」。</summary>
    public string Source { get => _source; set => Set(ref _source, value); }

    /// <summary>「點擊複製」或「已複製」。</summary>
    public string Action { get => _action; set => Set(ref _action, value); }

    public bool HasCode { get => _hasCode; set => Set(ref _hasCode, value); }

    public ICommand CopyCommand { get; init; } = new RelayCommand(() => { });
}

/// <summary>收件匣頁面：左側最新驗證碼、右側最近的通知。</summary>
public sealed class InboxViewModel : ViewModelBase
{
    private bool _empty = true;

    public OtpViewModel Otp { get; init; } = new();
    public ObservableCollection<InboxItem> Items { get; } = [];

    public bool Empty { get => _empty; set => Set(ref _empty, value); }
}

public sealed record InboxItem(string Title, string Body, string Time);
