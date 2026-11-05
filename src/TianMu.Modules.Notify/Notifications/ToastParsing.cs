using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TianMu.Modules.Notify.Notifications;

/// <summary>一則系統通知。</summary>
public sealed record ToastNotice(
    long Id,
    string AppId,
    string AppName,
    string Title,
    string Body,
    string? Attribution,
    DateTime ArrivedAt);

/// <summary>解析通知的 XML 內容（Windows Toast 格式）。</summary>
public static class ToastPayload
{
    /// <summary>
    /// 取出標題、內文與來源標註。
    /// 第一段文字是標題、其餘是內文；placement="attribution" 的文字是來源（例如瀏覽器通知的網站名稱）。
    /// </summary>
    public static (string Title, string Body, string? Attribution)? Parse(string xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        var binding = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "binding");
        if (binding is null) return null;

        string? attribution = null;
        var texts = new List<string>();
        foreach (var text in binding.Elements().Where(e => e.Name.LocalName == "text"))
        {
            string value = text.Value.Trim();
            if (value.Length == 0) continue;
            if ((string?)text.Attribute("placement") == "attribution") attribution = value;
            else texts.Add(value);
        }

        if (texts.Count == 0) return null;
        return (texts[0], string.Join("\n", texts.Skip(1)), attribution);
    }
}

/// <summary>
/// 從通知文字裡找出驗證碼（一次性密碼）。
/// 為避免把訂單編號、金額、年份當成驗證碼，必須同時出現「驗證碼」之類的關鍵字。
/// </summary>
public static partial class OtpExtractor
{
    [GeneratedRegex(
        @"驗證碼|验证码|認證碼|认证码|確認碼|确认码|動態碼|动态码|校驗碼|校验码|安全碼|安全码|登入碼|登录码|一次性密碼|" +
        @"verification|verify|security code|one[- ]?time|\botp\b|passcode|login code|sign[- ]?in code|\bcode\b|" +
        @"確認コード|認証コード|인증",
        RegexOptions.IgnoreCase)]
    private static partial Regex Keyword();

    // 4~8 位數字，可用空白或連字號分成兩段（例如 482 913、482-913），前面可帶 G- 這類字首
    [GeneratedRegex(@"(?<![\d.,$¥€£])(?:[A-Z]-)?(?<code>\d{3,4}[ -]\d{3,4}|\d{4,8})(?![\d.,%])")]
    private static partial Regex Candidate();

    [GeneratedRegex(@"^(19|20)\d{2}$")]
    private static partial Regex LooksLikeYear();

    public static string? Find(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !Keyword().IsMatch(text)) return null;

        foreach (Match m in Candidate().Matches(text))
        {
            string code = m.Groups["code"].Value.Replace(" ", "").Replace("-", "");
            if (code.Length is < 4 or > 8) continue;
            if (LooksLikeYear().IsMatch(code)) continue;
            if (IsFollowedByUnit(text, m.Index + m.Length)) continue;
            return code;
        }
        return null;
    }

    /// <summary>「5 分鐘」「100 元」這種後面接單位的數字不是驗證碼。</summary>
    private static bool IsFollowedByUnit(string text, int end)
    {
        var rest = text.AsSpan(end).TrimStart();
        string[] units = ["分", "秒", "元", "塊", "min", "minute", "sec", "hour", "小時", "天", "日", "年", "月", "%", "MB", "GB"];
        foreach (string unit in units)
            if (rest.StartsWith(unit, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>顯示用：「482913」→「482 913」，較好唸。</summary>
    public static string Pretty(string code) => code.Length switch
    {
        6 => $"{code[..3]} {code[3..]}",
        8 => $"{code[..4]} {code[4..]}",
        _ => code,
    };
}
