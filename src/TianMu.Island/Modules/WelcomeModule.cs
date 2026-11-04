using TianMu.Core;

namespace TianMu.Island.Modules;

/// <summary>
/// 第一件內建法器：開機問候。
/// 它的存在是為了示範「法器 → 宿主 → 天幕動畫」整條管線已經接通。
/// 未來的音樂、通知、番茄鐘法器都照這個模式撰寫。
/// </summary>
internal sealed class WelcomeModule : IIslandModule
{
    public string Id => "tianmu.welcome";
    public string DisplayName => "開幕問候";

    public async Task InitializeAsync(IIslandHost host, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(1.2), cancellationToken);

        host.Post(new IslandActivity(
            ModuleId: Id,
            Title: "天幕已開",
            Subtitle: "靈台初明 · 道友安好",
            Priority: ActivityPriority.Normal,
            Duration: TimeSpan.FromSeconds(3.5)));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
