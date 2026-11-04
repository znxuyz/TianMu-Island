using System.Windows;
using TianMu.Island.Modules;

namespace TianMu.Island;

public partial class App : Application
{
    // 一山不容二虎：同時只允許一個天幕島執行
    private Mutex? _singleInstance;
    private ModuleManager? _modules;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\TianMu-Island.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        var window = new IslandWindow();
        MainWindow = window;
        window.Show();

        // 祭煉所有法器（內建 + modules 資料夾中的外掛 DLL）
        _modules = new ModuleManager(window);
        await _modules.LoadAllAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 給法器一點時間收功；不阻塞太久
        _modules?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
