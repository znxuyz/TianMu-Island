using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace TianMu.Modules.Cleanse.Views;

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(); }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>審查清單的一列。</summary>
public sealed class CleanseRow : ViewModelBase
{
    private readonly Action _changed;
    private bool _isChecked;
    private string _status = "";

    public CleanseRow(CleanseItem item, Action changed)
    {
        Item = item;
        _changed = changed;
        _isChecked = item.Tier == CleanseTier.Primary;   // 可清的預設勾選；你的檔案預設不勾
        OpenCommand = new RelayCommand(Open);
    }

    public CleanseItem Item { get; }
    public string Name => Item.Name;
    public string SizeText => CleanseJudge.Size(Item.Bytes) + (Item.Partial ? "+" : "");
    public string Reason => Item.Reason;
    public string ShownPath => Item.Kind == CleanseKind.RecycleBin ? "" : Item.Path;
    public bool CanCheck => Item.Tier != CleanseTier.Keep && Status.Length == 0;
    public bool CanOpen => Item.Kind != CleanseKind.RecycleBin;
    public ICommand OpenCommand { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set { if (Set(ref _isChecked, value && CanCheck)) _changed(); }
    }

    /// <summary>清理後的結果（例如「已清理 · 1.2 GB」）；有結果之後就不能再勾。</summary>
    public string Status
    {
        get => _status;
        set
        {
            if (!Set(ref _status, value)) return;
            Raise(nameof(CanCheck));
            if (value.Length > 0 && _isChecked) { _isChecked = false; Raise(nameof(IsChecked)); }
        }
    }

    /// <summary>在檔案總管裡打開並選取它，先看看裡面是什麼。</summary>
    private void Open()
    {
        try
        {
            var psi = System.IO.File.Exists(Item.Path) || System.IO.Directory.Exists(Item.Path)
                ? new ProcessStartInfo("explorer.exe", $"/select,\"{Item.Path}\"")
                : new ProcessStartInfo("explorer.exe", $"\"{System.IO.Path.GetDirectoryName(Item.Path)}\"");
            psi.UseShellExecute = true;
            Process.Start(psi);
        }
        catch (Exception e) { Debug.WriteLine($"[洗髓] 開啟位置失敗：{e.Message}"); }
    }
}

/// <summary>一個等級的分組（可清／你決定／不動）。</summary>
public sealed class CleanseGroup : ViewModelBase
{
    private string _total = "";

    public CleanseGroup(CleanseTier tier, string title, string hint)
    {
        Tier = tier;
        Title = title;
        Hint = hint;
        Rows.CollectionChanged += (_, _) => { Raise(nameof(Visibility)); Refresh(); };
    }

    public CleanseTier Tier { get; }
    public string Title { get; }
    public string Hint { get; }
    public ObservableCollection<CleanseRow> Rows { get; } = [];
    public Visibility Visibility => Rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string Total { get => _total; private set => Set(ref _total, value); }

    public void Refresh() => Total = $"{Rows.Count} 項 · {CleanseJudge.Size(Rows.Sum(r => r.Item.Bytes))}";
}

/// <summary>洗髓視窗：掃描 → 審查 → 清理。</summary>
public sealed class CleanseViewModel : ViewModelBase
{
    private readonly Func<bool> _optionalToRecycleBin;
    private readonly Action<string, string> _notify;
    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _confirming;
    private string _status = "按「掃描」看看有哪些可以清";
    private string _diskText = "";
    private double _used;
    private bool _low;
    private string _selected = "";
    private string _summary = "";

    public CleanseViewModel(Func<bool> optionalToRecycleBin, Action<string, string> notify)
    {
        _optionalToRecycleBin = optionalToRecycleBin;
        _notify = notify;
        Groups =
        [
            new(CleanseTier.Primary, "可清", "會自動重建的快取與暫存；預設已勾選"),
            new(CleanseTier.Optional, "你決定", "你自己的檔案，或重新下載代價高的東西；預設不勾，清理時先移到資源回收筒"),
            new(CleanseTier.Keep, "不動", "受保護或看起來還在用的，只列出來讓你知道佔了多少"),
        ];
        ScanCommand = new RelayCommand(() => _ = ScanAsync(), () => !IsBusy);
        CleanCommand = new RelayCommand(Clean, () => !IsBusy && SelectedCount > 0);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
        RefreshDisk();
    }

    public ObservableCollection<CleanseGroup> Groups { get; }
    public RelayCommand ScanCommand { get; }
    public RelayCommand CleanCommand { get; }
    public RelayCommand CancelCommand { get; }

    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (!Set(ref _busy, value)) return;
            ScanCommand.Refresh();
            CleanCommand.Refresh();
            CancelCommand.Refresh();
        }
    }

    public string Status { get => _status; private set => Set(ref _status, value); }
    public string DiskText { get => _diskText; private set => Set(ref _diskText, value); }
    public double UsedFraction { get => _used; private set => Set(ref _used, value); }
    public bool DiskLow { get => _low; private set => Set(ref _low, value); }
    public string SelectedText { get => _selected; private set => Set(ref _selected, value); }
    public string Summary { get => _summary; private set => Set(ref _summary, value); }
    public string CleanLabel => _confirming ? "再按一次確認清理" : "清理所選";

    private IEnumerable<CleanseRow> AllRows => Groups.SelectMany(g => g.Rows);
    private int SelectedCount => AllRows.Count(r => r.IsChecked);

    private void RefreshDisk()
    {
        var (free, total) = DiskScanner.VolumeStatus();
        if (total <= 0) { DiskText = ""; return; }
        DiskText = $"{DiskScanner.SystemVolume.TrimEnd('\\')} 剩 {CleanseJudge.Size(free)}，共 {CleanseJudge.Size(total)}";
        UsedFraction = 1 - free / (double)total;
        DiskLow = free < 15 * CleanseRules.GB;
    }

    private void OnSelectionChanged()
    {
        var picked = AllRows.Where(r => r.IsChecked).ToList();
        SelectedText = picked.Count == 0 ? "還沒有勾選任何項目" : $"已勾選 {picked.Count} 項，約可釋放 {CleanseJudge.Size(picked.Sum(r => r.Item.Bytes))}";
        SetConfirming(false);
        CleanCommand.Refresh();
    }

    private void SetConfirming(bool on)
    {
        if (_confirming == on) return;
        _confirming = on;
        Raise(nameof(CleanLabel));
    }

    public async Task ScanAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        SetConfirming(false);
        foreach (var g in Groups) g.Rows.Clear();
        Summary = "";
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(s => Status = s);
        try
        {
            var result = await Task.Run(() => new DiskScanner().Scan(progress, _cts.Token));
            foreach (var item in result.Items)
                Groups.First(g => g.Tier == item.Tier).Rows.Add(new CleanseRow(item, OnSelectionChanged));
            long canFree = result.Items.Where(i => i.Tier != CleanseTier.Keep).Sum(i => i.Bytes);
            Status = result.Items.Count == 0 ? "沒找到值得清的東西，很乾淨" : $"找到 {result.Items.Count} 項，最多約可釋放 {CleanseJudge.Size(canFree)}";
        }
        catch (OperationCanceledException) { Status = "已停止掃描"; }
        catch (Exception e) { Status = $"掃描失敗：{e.Message}"; }
        finally
        {
            IsBusy = false;
            RefreshDisk();
            OnSelectionChanged();
        }
    }

    /// <summary>清理按兩次：第一次變成「再按一次確認」，避免誤按。</summary>
    private void Clean()
    {
        if (!_confirming) { SetConfirming(true); return; }
        _ = CleanAsync();
    }

    private async Task CleanAsync()
    {
        var rows = AllRows.Where(r => r.IsChecked).ToList();
        if (rows.Count == 0) return;
        IsBusy = true;
        SetConfirming(false);
        _cts = new CancellationTokenSource();
        bool recycle = _optionalToRecycleBin();
        var (freeBefore, _) = DiskScanner.VolumeStatus();
        long recycled = 0;
        try
        {
            int n = 0;
            foreach (var row in rows)
            {
                Status = $"清理中（{++n}/{rows.Count}）：{row.Name}";
                var r = await Task.Run(() => DiskCleaner.Clean(row.Item, recycle, _cts.Token));
                row.Status = r.Outcome switch
                {
                    CleanOutcome.Deleted => $"已清理 · {CleanseJudge.Size(r.FreedBytes)}",
                    CleanOutcome.Recycled => $"已移到資源回收筒 · {CleanseJudge.Size(r.FreedBytes)}",
                    _ => r.Message,
                };
                if (r.Outcome == CleanOutcome.Recycled) recycled += r.FreedBytes;
            }
        }
        catch (OperationCanceledException) { Status = "已停止清理"; }
        finally
        {
            IsBusy = false;
            RefreshDisk();
            var (freeAfter, _) = DiskScanner.VolumeStatus();
            long freed = Math.Max(0, freeAfter - freeBefore);
            Summary = recycled > 0
                ? $"這次釋放了 {CleanseJudge.Size(freed)}；另有 {CleanseJudge.Size(recycled)} 在資源回收筒，清空後才會釋放"
                : $"這次釋放了 {CleanseJudge.Size(freed)}";
            Status = "清理完成";
            _notify("洗髓完成", Summary);
            OnSelectionChanged();
        }
    }

    public void Cancel() => _cts?.Cancel();
}
