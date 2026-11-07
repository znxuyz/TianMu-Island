using System.Windows;
using TianMu.Core;
using TianMu.Island.Modules;
using TianMu.Island.Settings;
using TianMu.Island.Shell;
using TianMu.Island.Update;

namespace TianMu.Island;

public partial class App : Application
{
    private const string MutexName = @"Local\TianMu-Island.SingleInstance";
    private const string ShowSettingsEventName = @"Local\TianMu-Island.ShowSettings";

    private Mutex? _singleInstance;
    private EventWaitHandle? _showSettingsSignal;
    private RegisteredWaitHandle? _showSettingsWait;
    private ModuleManager? _modules;
    private TrayIcon? _tray;
    private IslandWindow? _island;
    private SettingsWindow? _settingsWindow;

    public SettingsStore Settings { get; private set; } = null!;
    public UpdateCoordinator Updates { get; private set; } = null!;
    public IReadOnlyList<ModuleDescriptor> ModuleCatalog => _modules?.Catalog ?? [];

    public static new App Current => (App)Application.Current;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!AcquireSingleInstance(restarting: e.Args.Contains("--restart")))
        {
            // 已經有一個天幕島在執行：請它打開設定面板，自己安靜離開
            try
            {
                if (EventWaitHandle.TryOpenExisting(ShowSettingsEventName, out var signal))
                {
                    signal.Set();
                    signal.Dispose();
                }
            }
            catch { /* 對方正在關閉，忽略 */ }
            Shutdown();
            return;
        }

        UpdateService.CleanupLeftovers();
        IslandWindow.RegisterMenuLayering();

        Settings = new SettingsStore();
        SyncAutoStart();

        _island = new IslandWindow(Settings);
        MainWindow = _island;
        _island.Show();

        Updates = new UpdateCoordinator(Settings, _island);

        _tray = new TrayIcon(this);
        ListenForShowSettings();

        // 祭煉所有法器（內建 + modules 資料夾中的外掛 DLL）
        _modules = new ModuleManager(_island, Settings);
        await _modules.LoadAllAsync();

        if (e.Args.Contains("--updated"))
        {
            _island.Post(new IslandActivity("tianmu.update", $"已更新至 v{UpdateService.CurrentVersion.ToString(3)}",
                "新版法寶已就位", ActivityPriority.Normal, TimeSpan.FromSeconds(4)));
        }

        _ = Updates.AutoCheckAsync();
    }

    // ═══════════════════════ 供系統匣與設定面板呼叫 ═══════════════════════

    public bool IsIslandVisible => _island is { UserHidden: false };

    public IReadOnlyList<QuickAction> QuickActions => _island?.QuickActions ?? [];

    public void ToggleIsland()
    {
        if (_island is null) return;
        bool hide = !_island.UserHidden;
        _island.SetUserHidden(hide);

        // 隱藏後告訴使用者怎麼叫回來
        if (hide)
            _tray?.ShowHint("天幕已隱藏",
                _island.HotkeyRegistered
                    ? "按 Ctrl + Alt + 空白鍵，或點一下系統匣的天幕島圖示，就能叫回來。"
                    : "點一下系統匣（右下角時鐘旁，可能收在 ^ 裡）的天幕島圖示，就能叫回來。");
    }

    /// <summary>天幕被隱藏時叫回來。</summary>
    public void ShowIsland()
    {
        if (_island is { UserHidden: true }) _island.SetUserHidden(false);
    }

    public void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
                _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>重新啟動天幕島（例如法器開關變更後）。</summary>
    public void Restart()
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "--restart") { UseShellExecute = false });
        Quit();
    }

    public void Quit()
    {
        _settingsWindow?.Close();
        _island?.Close();
        Shutdown();
    }

    // ═══════════════════════ 內部 ═══════════════════════

    private bool AcquireSingleInstance(bool restarting)
    {
        _singleInstance = new Mutex(initiallyOwned: true, MutexName, out bool isFirst);
        if (isFirst) return true;
        if (!restarting) return false;

        // 重新啟動 / 更新：等舊的天幕島結束
        try
        {
            return _singleInstance.WaitOne(TimeSpan.FromSeconds(10));
        }
        catch (AbandonedMutexException)
        {
            return true; // 舊程式沒有正常釋放，但已經結束了
        }
    }

    private void ListenForShowSettings()
    {
        _showSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
        _showSettingsWait = ThreadPool.RegisterWaitForSingleObject(
            _showSettingsSignal,
            (_, _) => Dispatcher.InvokeAsync(() =>
            {
                // 再次開啟天幕島：天幕若被隱藏就叫回來，並打開設定
                ShowIsland();
                ShowSettings();
            }),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    /// <summary>讓登錄檔與設定一致（使用者可能在 Windows 設定裡關掉了開機啟動）。</summary>
    private void SyncAutoStart()
    {
        bool registered = AutoStart.IsEnabled();
        if (registered) AutoStart.RefreshPathIfEnabled();
        if (Settings.Snapshot().StartWithWindows != registered)
            Settings.Update(s => s.StartWithWindows = registered);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showSettingsWait?.Unregister(null);
        _showSettingsSignal?.Dispose();
        _tray?.Dispose();
        Updates?.Dispose();

        // 給法器一點時間收功；不阻塞太久
        _modules?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
