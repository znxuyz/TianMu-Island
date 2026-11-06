namespace TianMu.Core;

/// <summary>法器可供使用者調整的一個開關選項。</summary>
/// <param name="Key">選項鍵，讀取時傳給 <see cref="IModuleSettings.GetBool"/>。</param>
/// <param name="Title">設定面板上的名稱。</param>
/// <param name="Description">一句話說明。</param>
/// <param name="DefaultValue">預設值。</param>
public sealed record ModuleOption(string Key, string Title, string Description, bool DefaultValue);

/// <summary>選擇題選項裡的一個選項。</summary>
/// <param name="Value">存檔用的值（讀取時 <see cref="IModuleSettings.GetString"/> 回傳它）。</param>
/// <param name="Label">畫面上的文字，例如「25 分」。</param>
public sealed record ModuleChoiceItem(string Value, string Label);

/// <summary>法器的一個選擇題選項（例如專注時長 25 / 45 / 60 分），在設定面板以一排按鈕呈現。</summary>
public sealed record ModuleChoice(string Key, string Title, string Description, IReadOnlyList<ModuleChoiceItem> Items, string DefaultValue);

/// <summary>法器的一個文字選項（例如「指定提醒時刻：10:00, 14:30」）。</summary>
public sealed record ModuleText(string Key, string Title, string Description, string DefaultValue, string Placeholder = "");

/// <summary>
/// 選擇性介面：實作它的法器會在設定面板中顯示說明與選項。
/// </summary>
public interface IDescribedModule
{
    /// <summary>一句話介紹這件法器。</summary>
    string Description { get; }

    /// <summary>使用者可調整的開關選項（可為空）。</summary>
    IReadOnlyList<ModuleOption> Options { get; }

    /// <summary>選擇題選項（可為空）。</summary>
    IReadOnlyList<ModuleChoice> Choices => [];

    /// <summary>文字選項（可為空）。</summary>
    IReadOnlyList<ModuleText> Texts => [];
}
