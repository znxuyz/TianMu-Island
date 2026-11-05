namespace TianMu.Core;

/// <summary>
/// 天幕（宿主）向法器開放的能力。除非另有說明，皆可從任何執行緒呼叫。
/// </summary>
public interface IIslandHost
{
    // ── 異象 ───────────────────────────────────

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

    // ── 設定與資料 ─────────────────────────────

    /// <summary>取得某法器的使用者選項。</summary>
    IModuleSettings GetModuleSettings(string moduleId);

    /// <summary>
    /// 法器專屬的資料資料夾（已建立），例如 %APPDATA%\TianMu-Island\data\tianmu.focus。
    /// 法器自行在裡面存放紀錄檔。
    /// </summary>
    string GetDataDirectory(string moduleId);

    // ── 常駐頁面 ───────────────────────────────

    /// <summary>
    /// 加入（或更新）一個常駐頁面：展開天幕後可用左右箭頭切換到它（例如「AI 額度」）。
    /// 只會用到 <see cref="IslandActivity.ExpandedView"/> 與 <see cref="IslandActivity.Title"/>；同一法器只保留一頁。
    /// 頁面大小固定（見 <see cref="IslandActivity.ExpandedView"/> 的說明）。
    /// </summary>
    void SetPage(IslandActivity page);

    /// <summary>移除某法器的常駐頁面。</summary>
    void RemovePage(string moduleId);

    // ── 天幕外觀 ───────────────────────────────

    /// <summary>設定修為境界，天幕外觀隨之進化。</summary>
    void SetRealm(IslandRealm realm);

    /// <summary>播放「渡劫」特效：三道雷光，金線沿邊緣走一圈。</summary>
    void PlayTribulation();

    // ── 捷徑與狀態 ─────────────────────────────

    /// <summary>在展開的預設面板加入捷徑按鈕。</summary>
    void AddQuickAction(QuickAction action);

    /// <summary>
    /// 設定預設面板捷徑列左側的一行狀態文字（例如「築基 · 中期 · 今日 50 分」）。傳 null 清除。
    /// </summary>
    void SetStatus(string moduleId, string? text);

    // ── 檔案拖放 ───────────────────────────────

    /// <summary>
    /// 註冊檔案拖放：使用者把檔案拖到天幕上時，天幕顯示 <paramref name="hint"/>，放開後呼叫 <paramref name="onDrop"/>（UI 執行緒）。
    /// </summary>
    void AddFileDropHandler(string moduleId, string hint, Action<IReadOnlyList<string>> onDrop);

    // ── 狀態燈 ─────────────────────────────────

    /// <summary>
    /// 在天幕右上角亮起（或熄滅）一盞小燈，例如「麥克風使用中」。
    /// <paramref name="indicator"/> 為 null 時熄滅。同一個 key 只會有一盞。
    /// </summary>
    void SetIndicator(string key, IslandIndicator? indicator);

    // ── 專注模式 ───────────────────────────────

    /// <summary>目前是否在專注中（番茄鐘進行時）。其他法器應避免在此時打擾。</summary>
    bool IsFocusMode { get; }

    /// <summary>專注模式切換時觸發（UI 執行緒）。</summary>
    event Action<bool>? FocusModeChanged;

    /// <summary>開啟或關閉專注模式。</summary>
    void SetFocusMode(bool enabled);
}
