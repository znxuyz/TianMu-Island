using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;

namespace TianMu.Modules.Shelf.Views;

public sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}

/// <summary>袋中的一件檔案。</summary>
public sealed class ShelfItem
{
    public ShelfItem(string path, ImageSource? icon, Action<ShelfItem> remove)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path) is { Length: > 0 } name ? name : path;
        Icon = icon;
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public string Path { get; }
    public string Name { get; }
    public ImageSource? Icon { get; }
    public ICommand RemoveCommand { get; }
}

public sealed class ShelfViewModel : INotifyPropertyChanged
{
    private string _title = "乾坤袋";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ShelfItem> Items { get; } = [];

    /// <summary>「乾坤袋 · 3 件」。</summary>
    public string Title { get => _title; set { if (_title == value) return; _title = value; Raise(); } }

    public ICommand ClearCommand { get; init; } = new RelayCommand(() => { });

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
