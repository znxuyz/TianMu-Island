using System.Text.RegularExpressions;

namespace TianMu.Modules.Office;

/// <summary>整理文字辨識的結果（純邏輯，方便測試）。</summary>
public static partial class OcrText
{
    /// <summary>
    /// Windows 的辨識結果會在每個字之間加空格（中文、日文也是）；把中日文字之間的空格拿掉，英文單字之間的保留。
    /// </summary>
    public static string Join(IEnumerable<string> lines)
    {
        var cleaned = lines.Select(l => CjkGap().Replace(l.Trim(), string.Empty)).Where(l => l.Length > 0);
        return string.Join(Environment.NewLine, cleaned);
    }

    /// <summary>算字數：中日文每個字算一個，英文以單字計。</summary>
    public static int CountWords(string text) =>
        CjkChar().Matches(text).Count + LatinWord().Matches(text).Count;

    // 前後都是中日文（或全形標點）時，中間的空白拿掉
    [GeneratedRegex(@"(?<=[\p{IsCJKUnifiedIdeographs}\p{IsHiragana}\p{IsKatakana}\p{IsCJKSymbolsandPunctuation}\p{IsHalfwidthandFullwidthForms}])\s+(?=[\p{IsCJKUnifiedIdeographs}\p{IsHiragana}\p{IsKatakana}\p{IsCJKSymbolsandPunctuation}\p{IsHalfwidthandFullwidthForms}])")]
    private static partial Regex CjkGap();

    [GeneratedRegex(@"[\p{IsCJKUnifiedIdeographs}\p{IsHiragana}\p{IsKatakana}]")]
    private static partial Regex CjkChar();

    [GeneratedRegex(@"[A-Za-z0-9]+(?:['’.-][A-Za-z0-9]+)*")]
    private static partial Regex LatinWord();
}
