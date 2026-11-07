namespace TianMu.Core;

/// <summary>
/// 修為境界。天幕的外觀會隨境界多出細節（見 docs/DESIGN.md「修為五境」）。
/// </summary>
public enum IslandRealm
{
    /// <summary>練氣：素漆，月白靈光。</summary>
    LianQi = 0,
    /// <summary>築基：靈光轉金。</summary>
    ZhuJi = 1,
    /// <summary>金丹：中心透出金丹光暈。</summary>
    JinDan = 2,
    /// <summary>元嬰：靈氣外溢，金塵更多更亮。</summary>
    YuanYing = 3,
    /// <summary>化神：全身描金，落下朱砂神印。</summary>
    HuaShen = 4,
}

/// <summary>
/// 天幕展開時，預設面板下方的捷徑按鈕（例如「凝神」、「飲水」）。
/// </summary>
/// <param name="ModuleId">提供捷徑的法器。</param>
/// <param name="Label">按鈕文字（兩個字最好看）。</param>
/// <param name="Invoke">按下時執行（在 UI 執行緒）。</param>
/// <param name="Order">排序，數字小的在左。</param>
public sealed record QuickAction(string ModuleId, string Label, Action Invoke, int Order = 0);

/// <summary>
/// 法陣盤（長按天幕展開的圓形功能選單）上的一個扇區。
/// </summary>
/// <param name="ModuleId">提供它的法器。</param>
/// <param name="Label">扇區上的大字，兩三個字最好，例如「凝神」。</param>
/// <param name="Caption">大字下方的小字，例如「專注」。</param>
/// <param name="Invoke">選中時要做的事（UI 執行緒）。</param>
/// <param name="Order">排列順序，從正上方開始順時針，小的在前。</param>
/// <param name="ToneKey">文字顏色（主題色鍵）；null 為月白。</param>
/// <param name="OnFiles">拖著檔案放到這一格時要做的事（例如乾坤袋收下檔案）；null 表示這一格不收檔案。</param>
public sealed record RadialAction(string ModuleId, string Label, string Caption, Action Invoke, int Order = 0, string? ToneKey = null,
    Action<IReadOnlyList<string>>? OnFiles = null);
