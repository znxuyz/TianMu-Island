namespace TianMu.Core;

/// <summary>
/// 天幕（宿主）向法器開放的能力。除非另有說明，皆可從任何執行緒呼叫。
/// </summary>
public interface IIslandHost
{
    /// <summary>
    /// 在天幕上顯化一則異象。
    /// <see cref="ActivityPriority.Ambient"/> 會常駐在收合的膠囊中（同一法器只保留最新一則）；
    /// 其他優先級會自動展開，同時間只顯示一則，低優先級不能壓過高優先級。
    /// </summary>
    void Post(IslandActivity activity);

    /// <summary>撤回某法器目前顯化中的所有異象（常駐與展開的都會撤回）。</summary>
    void Dismiss(string moduleId);

    /// <summary>
    /// 在 UI 執行緒上執行動作。建立或修改自訂畫面（WPF 元件）時必須透過它。
    /// </summary>
    Task InvokeAsync(Action action);
}
