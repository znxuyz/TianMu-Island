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

    public SettingsViewModel(SettingsStore store, UpdateCoordinator updates, IReadOnlyList<ModuleDescriptor> catalog)
    {
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
                foreach (var o in m.Options) ms.Options[o.Key] = o.Value;
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
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public bool EnabledAtStartup { get; }
    public ObservableCollection<OptionRowViewModel> Options { get; } = [];

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

/// <summary>法器底下的一個選項。</summary>
public sealed class OptionRowViewModel(ModuleOption option, bool initialValue, Action changed) : Observable
{
    private bool _value = initialValue;
    private bool _parentEnabled = true;

    public string Key => option.Key;
    public string Title => option.Title;
    public string Description => option.Description;

    public bool Value
    {
        get => _value;
        set
        {
            if (Set(ref _value, value)) changed();
        }
    }

    /// <summary>法器本身被關閉時，選項跟著變灰。</summary>
    public bool ParentEnabled { get => _parentEnabled; set => Set(ref _parentEnabled, value); }
}
