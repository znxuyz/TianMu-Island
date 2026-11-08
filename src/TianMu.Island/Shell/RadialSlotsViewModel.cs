using System.Collections.ObjectModel;
using System.Windows.Input;
using TianMu.Core;
using TianMu.Island.Settings;

namespace TianMu.Island.Shell;

/// <summary>
/// 設定面板「法陣盤」一節：挑 4 到 10 個扇區、調整順序。
/// 放上盤的排在前面（依盤上順時針的順序），沒放上的排在後面。每次變動立刻存檔，下次長按就生效。
/// </summary>
public sealed class RadialSlotsViewModel : Observable
{
    private readonly SettingsStore _store;
    private readonly IReadOnlyList<RadialAction> _catalog;

    public RadialSlotsViewModel(SettingsStore store, IReadOnlyList<RadialAction> catalog)
    {
        _store = store;
        _catalog = catalog;
        ResetCommand = new Command(Reset);
        Load(store.Read(s => s.RadialSlots));
    }

    public ObservableCollection<RadialSlotRow> Rows { get; } = [];

    /// <summary>盤上的扇區（給小樣預覽）。</summary>
    public IReadOnlyList<string> Chosen => Rows.Where(r => r.IsOn).Select(r => r.Label).ToList();

    public int Count => Rows.Count(r => r.IsOn);

    public string CountText => $"盤上 {Count} 格（最少 {RadialLayout.Min}、最多 {RadialLayout.Max}）";

    public bool HasCatalog => Rows.Count > 0;

    public ICommand ResetCommand { get; }

    private void Load(IReadOnlyList<string>? saved)
    {
        Rows.Clear();
        var chosen = RadialLayout.Arrange(
            _catalog.Select(a => new RadialLayout.Candidate(a.Label, a.Order, a.DefaultVisible)), saved);
        foreach (var key in chosen)
            Rows.Add(new RadialSlotRow(this, _catalog.First(a => a.Label == key), true));
        foreach (var a in _catalog.Where(a => !chosen.Contains(a.Label)).OrderBy(a => a.Order))
            Rows.Add(new RadialSlotRow(this, a, false));
        Changed(save: false);
    }

    private void Reset()
    {
        _store.Update(s => s.RadialSlots = null);
        Load(null);
    }

    internal void Toggle(RadialSlotRow row, bool on)
    {
        var chosen = Chosen.ToList();
        if (!RadialLayout.TryToggle(chosen, row.Label, on)) { row.Refresh(); return; }

        // 放上盤的移到已選區的最後；拿下來的移到未選區的最前
        Rows.Remove(row);
        row.SetOn(on);
        int lastOn = Rows.Count(r => r.IsOn);
        Rows.Insert(lastOn, row);
        Changed(save: true);
    }

    internal void Move(RadialSlotRow row, int delta)
    {
        int i = Rows.IndexOf(row), j = i + delta;
        if (!row.IsOn || j < 0 || j >= Count) return;
        Rows.Move(i, j);
        Changed(save: true);
    }

    internal bool CanMove(RadialSlotRow row, int delta)
    {
        int j = Rows.IndexOf(row) + delta;
        return row.IsOn && j >= 0 && j < Count;
    }

    internal bool CanToggle(RadialSlotRow row) => row.IsOn ? Count > RadialLayout.Min : Count < RadialLayout.Max;

    private void Changed(bool save)
    {
        if (save)
        {
            var chosen = Chosen.ToList();
            _store.Update(s => s.RadialSlots = chosen);
        }
        foreach (var r in Rows) r.Refresh();
        Raise(nameof(Chosen));
        Raise(nameof(Count));
        Raise(nameof(CountText));
        Raise(nameof(HasCatalog));
    }

    /// <summary>簡單的指令（設定面板不需要 CanExecute 變化通知以外的功能）。</summary>
    internal sealed class Command(Action run, Func<bool>? can = null) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => can?.Invoke() ?? true;
        public void Execute(object? parameter) { if (CanExecute(parameter)) run(); }
        public void NotifyChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>法陣盤設定裡的一列：一個候選扇區。</summary>
public sealed class RadialSlotRow : Observable
{
    private readonly RadialSlotsViewModel _owner;
    private readonly RadialSlotsViewModel.Command _up, _down;
    private bool _isOn;

    internal RadialSlotRow(RadialSlotsViewModel owner, RadialAction action, bool isOn)
    {
        _owner = owner;
        Label = action.Label;
        Caption = action.Caption;
        _isOn = isOn;
        _up = new RadialSlotsViewModel.Command(() => _owner.Move(this, -1), () => _owner.CanMove(this, -1));
        _down = new RadialSlotsViewModel.Command(() => _owner.Move(this, +1), () => _owner.CanMove(this, +1));
    }

    public string Label { get; }
    public string Caption { get; }

    public bool IsOn
    {
        get => _isOn;
        set { if (value != _isOn) _owner.Toggle(this, value); }
    }

    public bool CanToggle => _owner.CanToggle(this);

    /// <summary>在盤上的位置（1 起算，從正上方順時針）；沒放上時是空字串。</summary>
    public string Position => IsOn ? (_owner.Rows.IndexOf(this) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

    public ICommand MoveUp => _up;
    public ICommand MoveDown => _down;

    internal void SetOn(bool on) => _isOn = on;

    internal void Refresh()
    {
        Raise(nameof(IsOn));
        Raise(nameof(CanToggle));
        Raise(nameof(Position));
        _up.NotifyChanged();
        _down.NotifyChanged();
    }
}
