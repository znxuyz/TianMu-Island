namespace TianMu.Core;

/// <summary>
/// 法器模組（外掛）契約。
/// 音樂、通知、番茄鐘……每一個功能都是一件獨立的「法器」，
/// 主程式（天幕）只負責視窗、渲染與物理動畫，透過 <see cref="IIslandHost"/> 接收法器送來的「異象」。
/// </summary>
public interface IIslandModule : IAsyncDisposable
{
    /// <summary>唯一識別碼，例如 "tianmu.music"。</summary>
    string Id { get; }

    /// <summary>顯示名稱，例如「萬象樂律」。</summary>
    string DisplayName { get; }

    /// <summary>
    /// 祭煉（初始化）。在背景執行緒呼叫，可安全地進行 I/O 或長時間監聽。
    /// </summary>
    Task InitializeAsync(IIslandHost host, CancellationToken cancellationToken);
}
