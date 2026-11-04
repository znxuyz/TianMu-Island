namespace TianMu.Core;

/// <summary>
/// 天幕（宿主）向法器開放的能力。可從任何執行緒呼叫，宿主會自行切回 UI 執行緒。
/// </summary>
public interface IIslandHost
{
    /// <summary>在天幕上顯化一則異象（活動）。同優先級時後到者覆蓋先到者。</summary>
    void Post(IslandActivity activity);

    /// <summary>撤回某法器目前顯化中的異象。</summary>
    void Dismiss(string moduleId);
}
