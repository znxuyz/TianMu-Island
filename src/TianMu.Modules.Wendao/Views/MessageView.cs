using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TianMu.Modules.Wendao.Ai;

namespace TianMu.Modules.Wendao.Views;

/// <summary>對話裡的一則訊息：你的問題靠右、AI 的回答靠左；程式碼區塊用等寬字並附「複製」。</summary>
internal sealed class MessageView : Border
{
    private readonly StackPanel _body = new();
    private readonly TextBlock _header;
    private readonly Role _role;
    private string _text = "";
    private TextBox? _streaming;

    public MessageView(Role role, string text, string? header, byte[]? image = null, bool hadImage = false)
    {
        _role = role;
        bool user = role == Role.User;
        HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        MaxWidth = user ? 620 : double.PositiveInfinity;
        Margin = new Thickness(user ? 80 : 0, 6, 0, 6);
        Padding = new Thickness(14, 10, 14, 10);
        CornerRadius = new CornerRadius(10);
        Background = user ? Look.UserBubble : Look.Panel;
        BorderBrush = Look.Hairline;
        BorderThickness = new Thickness(user ? 0 : 1);

        var outer = new StackPanel();
        _header = new TextBlock { Text = header ?? "", FontSize = 11.5, Foreground = Look.Gold, Margin = new Thickness(0, 0, 0, 6), FontFamily = Look.Sans, Visibility = string.IsNullOrEmpty(header) ? Visibility.Collapsed : Visibility.Visible };
        outer.Children.Add(_header);
        if (image is { Length: > 0 } && Bitmap(image) is { } bmp)
            outer.Children.Add(new Image { Source = bmp, MaxHeight = 220, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) });
        else if (hadImage)
            outer.Children.Add(new TextBlock { Text = "（附了一張圖）", Foreground = Look.Tertiary, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });
        outer.Children.Add(_body);
        Child = outer;
        SetText(text);
    }

    public string Text => _text;

    public void SetHeader(string text, bool warn = false)
    {
        _header.Text = text;
        _header.Foreground = warn ? Look.Cinnabar : Look.Gold;
        _header.Visibility = Visibility.Visible;
    }

    /// <summary>串流時只更新一個文字框（快）；結束後呼叫 <see cref="SetText"/> 排版程式碼區塊。</summary>
    public void Append(string piece)
    {
        _text += piece;
        if (_streaming is null)
        {
            _body.Children.Clear();
            _streaming = Look.ReadOnlyText(_text);
            _body.Children.Add(_streaming);
        }
        else _streaming.AppendText(piece);
    }

    public void SetText(string text)
    {
        _text = text;
        _streaming = null;
        _body.Children.Clear();
        foreach (var (isCode, lang, content) in Split(text))
        {
            if (!isCode)
            {
                if (content.Trim().Length > 0) _body.Children.Add(Look.ReadOnlyText(content.Trim('\n'), size: 14, fg: _role == Role.User ? Look.Moon : Look.Moon));
                continue;
            }
            var code = Look.ReadOnlyText(content.TrimEnd('\n'), Look.Mono, 13, Look.Moon);
            code.TextWrapping = TextWrapping.NoWrap;
            code.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            string captured = content;
            var copy = Look.Button("複製", () => { try { Clipboard.SetText(captured.TrimEnd('\n')); } catch (Exception) { } });
            copy.Padding = new Thickness(8, 2, 8, 2);
            copy.FontSize = 11.5;
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(copy, Dock.Right);
            bar.Children.Add(copy);
            bar.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(lang) ? "程式碼" : lang, Foreground = Look.Tertiary, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center });
            var stack = new StackPanel();
            stack.Children.Add(bar);
            stack.Children.Add(code);
            _body.Children.Add(new Border { Background = Look.CodeBg, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 10), Margin = new Thickness(0, 6, 0, 6), Child = stack });
        }
    }

    /// <summary>依 ``` 把文字切成一般段落與程式碼區塊（純邏輯）。</summary>
    public static List<(bool IsCode, string Lang, string Text)> Split(string text)
    {
        var parts = new List<(bool, string, string)>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var buf = new System.Text.StringBuilder();
        bool inCode = false;
        string lang = "";
        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                parts.Add((inCode, lang, buf.ToString()));
                buf.Clear();
                lang = inCode ? "" : line.Trim()[3..].Trim();
                inCode = !inCode;
                continue;
            }
            buf.Append(line).Append('\n');
        }
        parts.Add((inCode, lang, buf.ToString()));
        return parts.Where(p => p.Item3.Trim().Length > 0).ToList();
    }

    private static BitmapSource? Bitmap(byte[] bytes)
    {
        try
        {
            var b = new BitmapImage();
            b.BeginInit();
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.StreamSource = new System.IO.MemoryStream(bytes);
            b.EndInit();
            b.Freeze();
            return b;
        }
        catch (Exception) { return null; }
    }
}
