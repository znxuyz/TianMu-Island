using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using TianMu.Core;
using TianMu.Island.Modules;
using TianMu.Island.Settings;
using TianMu.Island.Update;

namespace TianMu.Island.Shell;

/// <summary>可通知的基底類別。</summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>設定面板的資料。</summary>
public sealed class SettingsViewModel : Observable
{
    private readonly SettingsStore _store;
    private readonly UpdateCoordinator _updates;
    private bool _startWithWindows;
    private bool _showParticles;
    private bool _autoCheckUpdates;
    private bool _needsRestart;
    private string? _notice;

    public SettingsViewModel(SettingsStore store, UpdateCoordinator updates, IReadOnlyList<ModuleDescriptor> catalog,
        IReadOnlyList<RadialAction> radialCatalog)
    {
        Radial = new RadialSlotsViewModel(store, radialCatalog);
        _store = store;
        _updates = updates;

        var s = store.Snapshot();
        _startWithWindows = s.StartWithWindows;
        _showParticles = s.ShowParticles;
        _autoCheckUpdates = s.AutoCheckUpdates;

        foreach (var d in catalog)
        {
            var module = new ModuleRowViewModel(d, s, OnModuleChanged);
            Modules.Add(module);
        }

        _updates.Changed += OnUpdatesChanged;
    }

    public ObservableCollection<ModuleRowViewModel> Modules { get; } = [];

    /// <summary>法陣盤要放哪些扇區。</summary>
    public RadialSlotsViewModel Radial { get; }

    public string VersionText => UpdateCoordinator.CurrentVersionText;

    // ── 一般 ───────────────────────────────────

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (_startWithWindows == value) return;
            if (!AutoStart.SetEnabled(value))
            {
                Notice = "無法變更開機啟動設定，請確認權限";
                Raise();
                return;
            }
            _startWithWindows = value;
            _store.Update(s => s.StartWithWindows = value);
            Raise();
        }
    }

    // 以下開關直接讀寫設定檔，不另存欄位
    public bool AutoHideFullscreen
    {
        get => _store.Read(s => s.AutoHideFullscreen);
        set => Save(_store.Read(s => s.AutoHideFullscreen), value, s => s.AutoHideFullscreen = value);
    }

    public bool Magnetic
    {
        get => _store.Read(s => s.Magnetic);
        set => Save(_store.Read(s => s.Magnetic), value, s => s.Magnetic = value);
    }

    public bool FollowCursorScreen
    {
        get => _store.Read(s => s.FollowCursorScreen);
        set => Save(_store.Read(s => s.FollowCursorScreen), value, s => s.FollowCursorScreen = value);
    }

    public bool DayNightTint
    {
        get => _store.Read(s => s.DayNightTint);
        set => Save(_store.Read(s => s.DayNightTint), value, s => s.DayNightTint = value);
    }

    public bool SmartHide
    {
        get => _store.Read(s => s.SmartHide);
        set => Save(_store.Read(s => s.SmartHide), value, s => s.SmartHide = value);
    }

    public bool SpringLively
    {
        get => _store.Read(s => s.SpringFeel) != SpringFeels.Crisp;
        set { if (value) SetSpringFeel(SpringFeels.Lively); }
    }

    public bool SpringCrisp
    {
        get => _store.Read(s => s.SpringFeel) == SpringFeels.Crisp;
        set { if (value) SetSpringFeel(SpringFeels.Crisp); }
    }

    private void SetSpringFeel(string feel)
    {
        if (_store.Read(s => s.SpringFeel) == feel) return;
        _store.Update(s => s.SpringFeel = feel);
        Raise(nameof(SpringLively));
        Raise(nameof(SpringCrisp));
    }

    // ── 設定畫面字體大小 ──
    public bool UiScaleNormal { get => IsUiScale(UiScales.Normal); set { if (value) SetUiScale(UiScales.Normal); } }
    public bool UiScaleLarge { get => IsUiScale(UiScales.Large); set { if (value) SetUiScale(UiScales.Large); } }
    public bool UiScaleLarger { get => IsUiScale(UiScales.Larger); set { if (value) SetUiScale(UiScales.Larger); } }
    public bool UiScaleHuge { get => IsUiScale(UiScales.Huge); set { if (value) SetUiScale(UiScales.Huge); } }

    /// <summary>目前的縮放倍率（設定視窗據此縮放整個畫面）。</summary>
    public double UiScale => UiScales.Scale(_store.Read(s => s.SettingsScale));

    private bool IsUiScale(string size) => (_store.Read(s => s.SettingsScale) ?? UiScales.Large) == size;

    private void SetUiScale(string size)
    {
        if (IsUiScale(size)) return;
        _store.Update(s => s.SettingsScale = size);
        Raise(nameof(UiScaleNormal));
        Raise(nameof(UiScaleLarge));
        Raise(nameof(UiScaleLarger));
        Raise(nameof(UiScaleHuge));
        Raise(nameof(UiScale));
    }

    public bool FocusBarrier
    {
        get => _store.Read(s => s.FocusBarrier);
        set => Save(_store.Read(s => s.FocusBarrier), value, s => s.FocusBarrier = value);
    }

    private void Save(bool current, bool value, Action<AppSettings> write, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (current == value) return;
        _store.Update(write);
        Raise(name);
    }

    public bool ShowParticles
    {
        get => _showParticles;
        set
        {
            if (!Set(ref _showParticles, value)) return;
            _store.Update(s => s.ShowParticles = value);
        }
    }

    // ── 法器 ───────────────────────────────────

    public bool NeedsRestart { get => _needsRestart; private set => Set(ref _needsRestart, value); }

    private void OnModuleChanged()
    {
        NeedsRestart = Modules.Any(m => m.Enabled != m.EnabledAtStartup);
        _store.Update(s =>
        {
            foreach (var m in Modules)
            {
                var ms = s.GetModule(m.Id);
                ms.Enabled = m.Enabled;
                foreach (var o in m.Options)
                {
                    switch (o)
                    {
                        case OptionRowViewModel b: ms.Options[b.Key] = b.Value; break;
                        case ChoiceRowViewModel c: ms.Values[c.Key] = c.Value; break;
                        case TextRowViewModel t: ms.Values[t.Key] = t.Value; break;
                    }
                }
            }
        });
    }

    // ── 更新 ───────────────────────────────────

    public bool AutoCheckUpdates
    {
        get => _autoCheckUpdates;
        set
        {
            if (!Set(ref _autoCheckUpdates, value)) return;
            _store.Update(s => s.AutoCheckUpdates = value);
        }
    }

    public string UpdateStatus => _updates.StatusText;
    public bool CanCheck => _updates.Stage is not (UpdateStage.Checking or UpdateStage.Downloading or UpdateStage.Installing);
    public bool CanInstall => _updates.Stage == UpdateStage.Available;
    public bool IsBusy => _updates.Stage is UpdateStage.Checking or UpdateStage.Downloading or UpdateStage.Installing;
    public double UpdateProgress => _updates.Progress;

    public Task CheckUpdatesAsync() => _updates.CheckAsync(manual: true);
    public Task InstallUpdateAsync() => _updates.InstallAsync();
    public void SkipUpdate() => _updates.SkipAvailable();
    public void OpenReleasePage() => _updates.OpenReleasePage();

    private void OnUpdatesChanged()
    {
        Raise(nameof(UpdateStatus));
        Raise(nameof(CanCheck));
        Raise(nameof(CanInstall));
        Raise(nameof(IsBusy));
        Raise(nameof(UpdateProgress));
    }

    // ── 關於 ───────────────────────────────────

    public string? Notice { get => _notice; set => Set(ref _notice, value); }

    public void OpenSettingsFolder() => Open(_store.Directory);

    public void OpenRepository() => Open($"https://github.com/{UpdateService.Owner}/{UpdateService.Repo}");

    private static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Debug.WriteLine($"[天幕] 無法開啟 {target}：{ex.Message}"); }
    }

    public void Detach() => _updates.Changed -= OnUpdatesChanged;
}

/// <summary>設定面板中的一件法器。</summary>
public sealed class ModuleRowViewModel : Observable
{
    private readonly Action _changed;
    private bool _enabled;

    public ModuleRowViewModel(ModuleDescriptor d, AppSettings s, Action changed)
    {
        _changed = changed;
        Id = d.Id;
        Title = d.DisplayName;
        Description = d.Description;
        EnabledAtStartup = d.EnabledAtStartup;
        _enabled = s.IsModuleEnabled(d.Id);

        s.Modules.TryGetValue(d.Id, out var saved);
        foreach (var option in d.Options)
        {
            bool value = option.DefaultValue;
            if (saved is not null && saved.Options.TryGetValue(option.Key, out bool v)) value = v;
            Options.Add(new OptionRowViewModel(option, value, changed) { ParentEnabled = _enabled });
        }
        foreach (var choice in d.Choices)
        {
            string value = saved is not null && saved.Values.TryGetValue(choice.Key, out var v) && v is not null ? v : choice.DefaultValue;
            Options.Add(new ChoiceRowViewModel(choice, value, changed) { ParentEnabled = _enabled });
        }
        foreach (var text in d.Texts)
        {
            string value = saved is not null && saved.Values.TryGetValue(text.Key, out var v) && v is not null ? v : text.DefaultValue;
            Options.Add(new TextRowViewModel(text, value, changed) { ParentEnabled = _enabled });
        }
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public bool EnabledAtStartup { get; }
    public ObservableCollection<OptionRowBase> Options { get; } = [];

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!Set(ref _enabled, value)) return;
            foreach (var o in Options) o.ParentEnabled = value;
            _changed();
        }
    }
}

/// <summary>法器底下的一個選項（開關、選擇題或文字）。</summary>
public abstract class OptionRowBase : Observable
{
    private bool _parentEnabled = true;

    public abstract string Key { get; }
    public abstract string Title { get; }
    public abstract string Description { get; }

    /// <summary>法器本身被關閉時，選項跟著變灰。</summary>
    public bool ParentEnabled { get => _parentEnabled; set => Set(ref _parentEnabled, value); }
}

/// <summary>開關選項。</summary>
public sealed class OptionRowViewModel(ModuleOption option, bool initialValue, Action changed) : OptionRowBase
{
    private bool _value = initialValue;

    public override string Key => option.Key;
    public override string Title => option.Title;
    public override string Description => option.Description;

    public bool Value
    {
        get => _value;
        set
        {
            if (Set(ref _value, value)) changed();
        }
    }
}

/// <summary>選擇題選項：一排按鈕，選中的那一個亮起。</summary>
public sealed class ChoiceRowViewModel : OptionRowBase
{
    private readonly ModuleChoice _choice;
    private readonly Action _changed;
    private string _value;

    public ChoiceRowViewModel(ModuleChoice choice, string initialValue, Action changed)
    {
        _choice = choice;
        _changed = changed;
        _value = choice.Items.Any(i => i.Value == initialValue) ? initialValue : choice.DefaultValue;
        Items = choice.Items.Select(i => new ChoiceItemViewModel(this, i)).ToList();
    }

    public override string Key => _choice.Key;
    public override string Title => _choice.Title;
    public override string Description => _choice.Description;
    public IReadOnlyList<ChoiceItemViewModel> Items { get; }

    public string Value
    {
        get => _value;
        set
        {
            if (!Set(ref _value, value)) return;
            foreach (var item in Items) item.Refresh();
            _changed();
        }
    }
}

/// <summary>選擇題裡的一個按鈕。</summary>
public sealed class ChoiceItemViewModel(ChoiceRowViewModel owner, ModuleChoiceItem item) : Observable
{
    public string Label => item.Label;

    public bool IsSelected
    {
        get => owner.Value == item.Value;
        set { if (value) owner.Value = item.Value; }
    }

    internal void Refresh() => Raise(nameof(IsSelected));
}

/// <summary>文字選項：離開輸入框時才存檔。</summary>
public sealed class TextRowViewModel(ModuleText text, string initialValue, Action changed) : OptionRowBase
{
    private string _value = initialValue;

    public override string Key => text.Key;
    public override string Title => text.Title;
    public override string Description => text.Description;
    public string Placeholder => text.Placeholder;

    public string Value
    {
        get => _value;
        set
        {
            if (Set(ref _value, value ?? string.Empty)) changed();
        }
    }
}
