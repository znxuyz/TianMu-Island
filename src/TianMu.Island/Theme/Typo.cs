using System.Windows;
using System.Windows.Controls;

namespace TianMu.Island.Theme;

/// <summary>
/// 排版輔助。WPF 的 TextBlock 沒有字距（letter-spacing）屬性，
/// 這裡用「字與字之間插入細空格（U+2009，約 0.2em）」模擬古籍排版的寬字距。
///
/// 用法：&lt;TextBlock theme:Typo.SpacedText="一般" /&gt;
/// </summary>
public static class Typo
{
    private const char ThinSpace = ' ';

    public static readonly DependencyProperty SpacedTextProperty = DependencyProperty.RegisterAttached(
        "SpacedText", typeof(string), typeof(Typo), new PropertyMetadata(null, OnSpacedTextChanged));

    public static string? GetSpacedText(DependencyObject d) => (string?)d.GetValue(SpacedTextProperty);
    public static void SetSpacedText(DependencyObject d, string? value) => d.SetValue(SpacedTextProperty, value);

    /// <summary>把每個字之間插入細空格（英數字詞保持相連）。</summary>
    public static string Space(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length * 2);
        for (int i = 0; i < text.Length; i++)
        {
            sb.Append(text[i]);
            if (i < text.Length - 1 && IsWide(text[i]) && text[i + 1] != ' ')
                sb.Append(ThinSpace);
        }
        return sb.ToString();
    }

    private static bool IsWide(char c) => c >= '⺀';

    private static void OnSpacedTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock tb) tb.Text = e.NewValue is string s ? Space(s) : string.Empty;
    }
}
