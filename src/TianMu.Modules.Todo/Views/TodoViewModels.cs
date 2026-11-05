using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace TianMu.Modules.Todo.Views;

public sealed class RelayCommand(Action execute) : ICommand
{
    public static readonly RelayCommand Empty = new(() => { });

    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}

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
}

/// <summary>到點時跳出的提醒。</summary>
public sealed class TodoReminderViewModel : ViewModelBase
{
    private string _text = string.Empty;
    private string _when = string.Empty;

    public string Text { get => _text; set => Set(ref _text, value); }

    /// <summary>「天機已至 · 15:30」。</summary>
    public string When { get => _when; set => Set(ref _when, value); }

    public ICommand DoneCommand { get; init; } = RelayCommand.Empty;
    public ICommand LaterCommand { get; init; } = RelayCommand.Empty;
}

/// <summary>「天機簿」頁裡的一行。</summary>
public sealed class TodoRow(string text, string when, bool overdue, Action done)
{
    public string Text { get; } = text;
    public string When { get; } = when;
    public bool Overdue { get; } = overdue;
    public ICommand DoneCommand { get; } = new RelayCommand(done);
}

/// <summary>「天機簿」頁：接下來的待辦。</summary>
public sealed class TodoPageViewModel : ViewModelBase
{
    private string _title = "天機簿";
    private string _more = string.Empty;

    public ObservableCollection<TodoRow> Rows { get; } = [];

    public string Title { get => _title; set => Set(ref _title, value); }

    /// <summary>放不下時的「還有 3 件」。</summary>
    public string More { get => _more; set => Set(ref _more, value); }

    public ICommand AddCommand { get; init; } = RelayCommand.Empty;
}
