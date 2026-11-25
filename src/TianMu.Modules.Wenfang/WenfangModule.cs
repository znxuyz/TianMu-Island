using TianMu.Core;
using TianMu.Modules.Wenfang.Shared;

namespace TianMu.Modules.Wenfang;

/// <summary>
/// 法器「文房」：天幕島內建的辦公三件套，不用另外安裝 Office。
///   · 墨書：文件（像 Word）
///   · 算籌：試算表（像 Excel）
///   · 畫卷：簡報（像 PowerPoint）
/// 法陣盤的「文房」扇區：點一下開起始頁；把 Word／Excel／PowerPoint 檔案拖進去就直接開。
/// </summary>
public sealed class WenfangModule : IIslandModule, IDescribedModule
{
    private IIslandHost? _host;

    public string Id => "tianmu.wenfang";
    public string DisplayName => "文房";
    public string Description => "內建的文件（墨書）、試算表（算籌）、簡報（畫卷），開得了 Word、Excel、PowerPoint 的檔案，不用另外安裝。";
    public IReadOnlyList<ModuleOption> Options => [];

    public Task InitializeAsync(IIslandHost host, CancellationToken cancellationToken)
    {
        _host = host;
        WenfangHub.Configure(host.GetDataDirectory(Id));
        host.AddRadialAction(new RadialAction(Id, "文房", "文件·表格·簡報", Open, Order: 55, ToneKey: "MiaoJinBrush",
            OnFiles: files => host.InvokeAsync(() => { foreach (var f in files) WenfangHub.Open(f); }), DefaultVisible: false));
        host.AddQuickAction(new QuickAction(Id, "文房", () => host.InvokeAsync(Open), Order: 55));
        return Task.CompletedTask;
    }

    private static void Open()
    {
        var w = new StartWindow();
        w.Show();
        // 從天幕或系統匣開啟時，Windows 常把新視窗壓在後面；暫時置頂一下確保跳到最前面
        w.Activate();
        w.Topmost = true;
        w.Topmost = false;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
