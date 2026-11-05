using TianMu.Core;

namespace TianMu.Island.Modules;

/// <summary>
/// 第一件內建法器：開機問候。
/// 它的存在是為了示範「法器 → 宿主 → 天幕動畫」整條管線已經接通。
/// </summary>
internal sealed class WelcomeModule : IIslandModule, IDescribedModule
{
    public string Id => "tianmu.welcome";
    public string DisplayName => "開幕問候";
    public string Description => "啟動時，天幕展開向你問好。";
    public IReadOnlyList<ModuleOption> Options => [];

    public Task InitializeAsync(IIslandHost host, CancellationToken cancellationToken)
    {
        // 初始化要盡快返回，不要擋住其他法器；延遲動作交給背景工作
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(1.2), cancellationToken);
            host.Post(new IslandActivity(
                ModuleId: Id,
                Title: "天幕已開",
                Subtitle: "靈台初明 · 道友安好",
                Priority: ActivityPriority.Normal,
                Duration: TimeSpan.FromSeconds(3.5)));
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
