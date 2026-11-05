namespace TianMu.Core;

/// <summary>法器可供使用者調整的一個開關選項。</summary>
/// <param name="Key">選項鍵，讀取時傳給 <see cref="IModuleSettings.GetBool"/>。</param>
/// <param name="Title">設定面板上的名稱。</param>
/// <param name="Description">一句話說明。</param>
/// <param name="DefaultValue">預設值。</param>
public sealed record ModuleOption(string Key, string Title, string Description, bool DefaultValue);

/// <summary>
/// 選擇性介面：實作它的法器會在設定面板中顯示說明與選項。
/// </summary>
public interface IDescribedModule
{
    /// <summary>一句話介紹這件法器。</summary>
    string Description { get; }

    /// <summary>使用者可調整的選項（可為空）。</summary>
    IReadOnlyList<ModuleOption> Options { get; }
}
