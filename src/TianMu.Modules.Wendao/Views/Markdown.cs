using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace TianMu.Modules.Wendao.Views;

/// <summary>
/// 簡易 Markdown 排版：標題、條列、編號、引言、**粗體**、`程式碼`。
/// 用唯讀的 RichTextBox 呈現，文字照樣可以選取、複製。
/// </summary>
internal static partial class Markdown
{
    public static RichTextBox Render(string text, Brush fg, double size = 14)
    {
        var doc = new FlowDocument { PagePadding = new Thickness(0), FontFamily = Look.Sans, FontSize = size, Foreground = fg, LineHeight = size * 1.6 };
        foreach (var raw in text.Replace("\r\n", "\n").Trim('\n').Split('\n'))
        {
            string line = raw.TrimEnd();
            var p = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
            var heading = Heading().Match(line);
            var bullet = Bullet().Match(line);
            var number = Numbered().Match(line);
            if (heading.Success)
            {
                int level = heading.Groups[1].Value.Length;
                p.FontSize = size + Math.Max(0, 6 - level * 1.5);
                p.FontWeight = FontWeights.Bold;
                p.Foreground = Look.Gold;
                p.Margin = new Thickness(0, 8, 0, 4);
                AddInline(p, heading.Groups[2].Value);
            }
            else if (bullet.Success)
            {
                p.Margin = new Thickness(18 + bullet.Groups[1].Value.Length * 8, 0, 0, 3);
                p.TextIndent = -14;
                p.Inlines.Add(new Run("•  ") { Foreground = Look.Gold });
                AddInline(p, bullet.Groups[2].Value);
            }
            else if (number.Success)
            {
                p.Margin = new Thickness(20 + number.Groups[1].Value.Length * 8, 0, 0, 3);
                p.TextIndent = -18;
                p.Inlines.Add(new Run(number.Groups[2].Value + ". ") { Foreground = Look.Gold });
                AddInline(p, number.Groups[3].Value);
            }
            else if (line.StartsWith('>'))
            {
                p.BorderBrush = Look.Gold;
                p.BorderThickness = new Thickness(2, 0, 0, 0);
                p.Padding = new Thickness(10, 0, 0, 0);
                p.Foreground = Look.Secondary;
                AddInline(p, line.TrimStart('>', ' '));
            }
            else if (Rule().IsMatch(line))
            {
                p.BorderBrush = Look.Hairline;
                p.BorderThickness = new Thickness(0, 0, 0, 1);
                p.Margin = new Thickness(0, 4, 0, 8);
            }
            else AddInline(p, line);
            doc.Blocks.Add(p);
        }
        return new RichTextBox
        {
            Document = doc, IsReadOnly = true, IsDocumentEnabled = false, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Padding = new Thickness(0), Foreground = fg, SelectionBrush = Look.Gold, IsTabStop = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    /// <summary>行內格式：**粗體**、`程式碼`、*斜體*。</summary>
    private static void AddInline(Paragraph p, string text)
    {
        int pos = 0;
        foreach (Match m in InlineToken().Matches(text))
        {
            if (m.Index > pos) p.Inlines.Add(new Run(text[pos..m.Index]));
            if (m.Groups["b"].Success) p.Inlines.Add(new Bold(new Run(m.Groups["b"].Value)) { Foreground = Look.Moon });
            else if (m.Groups["c"].Success) p.Inlines.Add(new Run(m.Groups["c"].Value) { FontFamily = Look.Mono, Background = Look.CodeBg, Foreground = Look.Gold });
            else if (m.Groups["i"].Success) p.Inlines.Add(new Italic(new Run(m.Groups["i"].Value)));
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) p.Inlines.Add(new Run(text[pos..]));
    }

    /// <summary>去掉 Markdown 記號（朗讀、匯出純文字用）。</summary>
    public static string Plain(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Select(l =>
        {
            var h = Heading().Match(l);
            if (h.Success) return h.Groups[2].Value;
            var b = Bullet().Match(l);
            if (b.Success) return b.Groups[2].Value;
            return l.TrimStart('>', ' ');
        });
        return InlineToken().Replace(string.Join("\n", lines), m => m.Groups["b"].Success ? m.Groups["b"].Value : m.Groups["c"].Success ? m.Groups["c"].Value : m.Groups["i"].Value);
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(\s*)[-*+]\s+(.*)$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^(\s*)(\d+)[.)]\s+(.*)$")]
    private static partial Regex Numbered();

    [GeneratedRegex(@"^\s*([-*_])\s*\1\s*\1[\s\-*_]*$")]
    private static partial Regex Rule();

    [GeneratedRegex(@"\*\*(?<b>[^*]+)\*\*|`(?<c>[^`]+)`|(?<![\w*])\*(?<i>[^*\s][^*]*)\*(?![\w*])")]
    private static partial Regex InlineToken();
}
