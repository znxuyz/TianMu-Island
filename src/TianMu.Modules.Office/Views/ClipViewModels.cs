using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;

namespace TianMu.Modules.Office.Views;

public sealed class RelayCommand(Action execute) : ICommand
{
    public static readonly RelayCommand Empty = new(() => { });
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}

/// <summary>萬象簿頁裡的一行。</summary>
public sealed class ClipRow(ClipEntry entry, Action<ClipEntry> copy, Action<ClipEntry> pin, Action<ClipEntry> remove)
{
    public string Badge => entry.Kind switch { ClipKind.Image => "圖", ClipKind.Files => "檔", _ => "文" };
    public ImageSource? Thumbnail => entry.Thumb ?? entry.Image;
    public bool HasThumbnail => entry.Image is not null;
    public string Preview => entry.Kind switch
    {
        ClipKind.Image => $"圖片 {entry.Image?.PixelWidth} × {entry.Image?.PixelHeight}",
        ClipKind.Files => entry.Files.Count == 1
            ? System.IO.Path.GetFileName(entry.Files[0])
            : $"{System.IO.Path.GetFileName(entry.Files[0])} 等 {entry.Files.Count} 個檔案",
        _ => ClipHistory.Preview(entry.Text),
    };
    public string When => entry.At.ToString("HH:mm", CultureInfo.InvariantCulture);
    public bool Pinned => entry.Pinned;
    public string PinGlyph => entry.Pinned ? "" : "";
    public string PinTip => entry.Pinned ? "取消釘選" : "釘選（不會被擠掉）";
    public ICommand CopyCommand { get; } = new RelayCommand(() => copy(entry));
    public ICommand PinCommand { get; } = new RelayCommand(() => pin(entry));
    public ICommand RemoveCommand { get; } = new RelayCommand(() => remove(entry));
}

public sealed class ClipPageViewModel : INotifyPropertyChanged
{
    private string _title = "萬象簿";
    private bool _empty = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ClipRow> Rows { get; } = [];
    public string Title { get => _title; set { if (_title == value) return; _title = value; Raise(); } }
    public bool Empty { get => _empty; set { if (_empty == value) return; _empty = value; Raise(); } }
    public ICommand ClearCommand { get; init; } = RelayCommand.Empty;

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
