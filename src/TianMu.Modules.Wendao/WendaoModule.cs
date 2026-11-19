using TianMu.Core;
using TianMu.Modules.Wendao.Ai;
using TianMu.Modules.Wendao.Store;
using TianMu.Modules.Wendao.Views;

namespace TianMu.Modules.Wendao;

/// <summary>
/// 法器「問道」：天幕島自己的 AI 轉接。直接連各家免費 API（不經過中間平台），
/// 可以手動選模型，或依問題性質自動挑選；某家額度用完就換下一家。
/// 法陣盤的「問道」扇區：點一下開對話視窗；把圖片拖進去就附上那張圖。
/// </summary>
public sealed class WendaoModule : IIslandModule, IDescribedModule
{
    private IIslandHost? _host;
    private ChatClient? _client;
    private Conductor? _conductor;
    private ChatWindow? _window;

    public string Id => "tianmu.wendao";
    public string DisplayName => "問道";
    public string Description => "一個視窗問各家免費 AI（Gemini、Groq、OpenRouter 免費模型、Mistral、本機 Ollama）；可手動選模型或依問題自動挑選，額度用完自動換下一家。";
    public IReadOnlyList<ModuleOption> Options => [];

    public Task InitializeAsync(IIslandHost host, CancellationToken cancellationToken)
    {
        _host = host;
        _client = new ChatClient();
        _conductor = new Conductor(_client, KeyStore.Get);
        host.AddRadialAction(new RadialAction(Id, "問道", "問各家 AI", () => Open(null), Order: 57, ToneKey: "MiaoJinBrush",
            OnFiles: files => host.InvokeAsync(() => Open(files.FirstOrDefault())), DefaultVisible: false));
        host.AddQuickAction(new QuickAction(Id, "問道", () => host.InvokeAsync(() => Open(null)), Order: 57));
        return Task.CompletedTask;
    }

    private void Open(string? file)
    {
        if (_window is null)
        {
            _window = new ChatWindow(_conductor!, _host?.GetDataDirectory(Id));
            _window.Closed += (_, _) => _window = null;
            _window.Show();
        }
        else
        {
            if (_window.WindowState == System.Windows.WindowState.Minimized) _window.WindowState = System.Windows.WindowState.Normal;
            _window.Activate();
        }
        if (file is not null) _window.AttachFile(file);
    }

    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        return ValueTask.CompletedTask;
    }
}
