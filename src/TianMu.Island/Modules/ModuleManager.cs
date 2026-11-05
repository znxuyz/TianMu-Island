using System.Diagnostics;
using System.IO;
using System.Reflection;
using TianMu.Core;
using TianMu.Island.Settings;

namespace TianMu.Island.Modules;

/// <summary>設定面板用的法器資訊。</summary>
public sealed record ModuleDescriptor(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyList<ModuleOption> Options,
    bool EnabledAtStartup);

/// <summary>
/// 法器總管：負責發現、祭煉（初始化）與收功（釋放）所有法器模組。
///
/// 來源有二：
///   1. 內建法器：<see cref="BuiltInAssemblies"/> 中所有實作 <see cref="IIslandModule"/> 的類別。
///   2. 外部法器：exe 同層 <c>modules\</c> 資料夾中的 *.dll。
/// 使用者在設定中關閉的法器不會被祭煉；任何一件法器出錯都只會被記錄，不會讓天幕崩潰。
/// </summary>
internal sealed class ModuleManager(IIslandHost host, SettingsStore settings) : IAsyncDisposable
{
    /// <summary>隨主程式一起打包的內建法器所在的組件。</summary>
    private static readonly Assembly[] BuiltInAssemblies =
    [
        typeof(ModuleManager).Assembly,                       // 開幕問候
        typeof(TianMu.Modules.Music.MusicModule).Assembly,    // 萬象樂律
        typeof(TianMu.Modules.Focus.FocusModule).Assembly,    // 專注陣法、清泉
        typeof(TianMu.Modules.Ai.AiQuotaModule).Assembly,     // AI 額度
    ];

    private readonly List<IIslandModule> _modules = [];
    private readonly List<ModuleDescriptor> _catalog = [];
    private readonly CancellationTokenSource _cts = new();

    /// <summary>所有找得到的法器（包含被關閉的）。</summary>
    public IReadOnlyList<ModuleDescriptor> Catalog => _catalog;

    public async Task LoadAllAsync()
    {
        var types = new List<Type>();
        foreach (var assembly in BuiltInAssemblies)
            types.AddRange(FindModuleTypes(assembly));

        string moduleDir = Path.Combine(AppContext.BaseDirectory, "modules");
        if (Directory.Exists(moduleDir))
        {
            foreach (string dll in Directory.EnumerateFiles(moduleDir, "*.dll"))
            {
                try
                {
                    types.AddRange(FindModuleTypes(Assembly.LoadFrom(dll)));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[天幕] 無法載入外部法器 {dll}: {ex.Message}");
                }
            }
        }

        var enabledMap = settings.Snapshot();

        foreach (var type in types)
        {
            IIslandModule module;
            try
            {
                module = (IIslandModule)Activator.CreateInstance(type)!;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[天幕] 法器 {type.FullName} 無法建立：{ex.Message}");
                continue;
            }

            bool enabled = enabledMap.IsModuleEnabled(module.Id);
            var described = module as IDescribedModule;
            _catalog.Add(new ModuleDescriptor(
                module.Id,
                module.DisplayName,
                described?.Description ?? string.Empty,
                described?.Options ?? [],
                enabled));

            if (!enabled)
            {
                await SafeDisposeAsync(module);
                continue;
            }

            try
            {
                // 在背景執行緒祭煉，避免卡住 UI
                await Task.Run(() => module.InitializeAsync(host, _cts.Token)).ConfigureAwait(true);
                _modules.Add(module);
                Debug.WriteLine($"[天幕] 法器已祭煉：{module.DisplayName} ({module.Id})");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[天幕] 法器 {module.Id} 祭煉失敗：{ex}");
                await SafeDisposeAsync(module);
            }
        }
    }

    private static IEnumerable<Type> FindModuleTypes(Assembly assembly)
    {
        Type[] all;
        try { all = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { all = ex.Types.Where(t => t is not null).ToArray()!; }

        return all.Where(t =>
            t is { IsClass: true, IsAbstract: false } &&
            typeof(IIslandModule).IsAssignableFrom(t) &&
            t.GetConstructor(Type.EmptyTypes) is not null);
    }

    private static async Task SafeDisposeAsync(IIslandModule module)
    {
        try { await module.DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex) { Debug.WriteLine($"[天幕] 法器 {module.Id} 收功失敗：{ex.Message}"); }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        foreach (var module in _modules)
            await SafeDisposeAsync(module);
        _modules.Clear();
        _cts.Dispose();
    }
}
