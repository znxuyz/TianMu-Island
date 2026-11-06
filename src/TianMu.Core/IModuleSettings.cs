namespace TianMu.Core;

/// <summary>
/// 法器的個人化選項（由使用者在設定面板中調整）。
/// 每次讀取都是最新值，法器不需要自己監聽變更。
/// </summary>
public interface IModuleSettings
{
    /// <summary>讀取布林選項；使用者從未設定過時回傳 <paramref name="defaultValue"/>。</summary>
    bool GetBool(string key, bool defaultValue);

    /// <summary>讀取選擇題或文字選項；使用者從未設定過時回傳 <paramref name="defaultValue"/>。</summary>
    string GetString(string key, string defaultValue) => defaultValue;
}
