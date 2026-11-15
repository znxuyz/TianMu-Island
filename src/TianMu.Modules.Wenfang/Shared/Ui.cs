using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TianMu.Modules.Wenfang.Shared;

/// <summary>文房各視窗共用的小工具：取主題色、做功能區按鈕與分組。</summary>
internal static class Ui
{
    public const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";

    private static ResourceDictionary? _styles;

    /// <summary>文房的功能區樣式（Themes/Wenfang.xaml）。</summary>
    public static ResourceDictionary Styles => _styles ??= new ResourceDictionary
    {
        Source = new Uri("pack://application:,,,/TianMu.Modules.Wenfang;component/Themes/Wenfang.xaml"),
    };

    public static Brush Brush(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    public static Brush Gold => Brush("MiaoJinBrush", Color.FromRgb(0xC8, 0xA8, 0x69));
    public static Brush Moon => Brush("YueBaiBrush", Color.FromRgb(0xEC, 0xE6, 0xD8));
    public static Brush Secondary => Brush("TextSecondaryBrush", Color.FromArgb(0x9E, 0xEC, 0xE6, 0xD8));
    public static Brush Tertiary => Brush("TextTertiaryBrush", Color.FromArgb(0x6B, 0xEC, 0xE6, 0xD8));
    public static Brush Hairline => Brush("HairlineBrush", Color.FromArgb(0x17, 0xEC, 0xE6, 0xD8));
    public static Brush Hover => Brush("HoverFillBrush", Color.FromArgb(0x14, 0xEC, 0xE6, 0xD8));
    public static Brush Lacquer => Brush("XuanQiBrush", Color.FromRgb(0x14, 0x11, 0x0E));
    public static Brush Cinnabar => Brush("ZhuShaBrush", Color.FromRgb(0xC0, 0x41, 0x2F));

    public static FontFamily Serif => Application.Current?.TryFindResource("SerifFont") as FontFamily ?? new FontFamily("serif");
    public static FontFamily Sans => Application.Current?.TryFindResource("SansFont") as FontFamily ?? new FontFamily("Microsoft JhengHei UI");

    public static TextBlock Glyph(string glyph, double size = 16) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily(IconFont),
        FontSize = size,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    /// <summary>大按鈕：圖示在上、字在下（像 Office 的「貼上」）。</summary>
    public static Button Big(string glyph, string label, Action click, string? tip = null)
    {
        var stack = new StackPanel();
        stack.Children.Add(Glyph(glyph, 20));
        stack.Children.Add(new TextBlock { Text = label, FontSize = 11, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
        var b = new Button { Content = stack, Padding = new Thickness(8, 4, 8, 4), MinWidth = 52, ToolTip = tip ?? label, Style = (Style)Styles["WfButton"] };
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>小按鈕：只有圖示（或一兩個字）。</summary>
    public static Button Small(string glyph, string tip, Action click, bool text = false)
    {
        var b = new Button
        {
            Content = text ? new TextBlock { Text = glyph, FontSize = 13 } : Glyph(glyph, 14),
            Width = 30, Height = 28, ToolTip = tip, Style = (Style)Styles["WfButton"],
        };
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>開關按鈕（粗體、置中…）：按下去會亮起描金框。</summary>
    public static ToggleButton Toggle(object content, string tip, Action<bool> changed)
    {
        var t = new ToggleButton
        {
            Content = content is string s ? new TextBlock { Text = s, FontSize = 14 } : content,
            Width = 30, Height = 28, ToolTip = tip, Style = (Style)Styles["WfToggle"],
        };
        t.Click += (_, _) => changed(t.IsChecked == true);
        return t;
    }

    /// <summary>功能區的一組（例如「字型」）：上面是按鈕，下面一行小字，右邊一條細線。</summary>
    public static FrameworkElement Group(string label, params UIElement[] rows)
    {
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var r in rows) body.Children.Add(r);
        var grid = new Grid { Margin = new Thickness(6, 4, 6, 2) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(body);
        var caption = new TextBlock { Text = label, FontSize = 10.5, Foreground = Tertiary, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
        Grid.SetRow(caption, 1);
        grid.Children.Add(caption);
        return new Border { Child = grid, BorderBrush = Hairline, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(0, 0, 4, 0) };
    }

    public static StackPanel Row(params UIElement[] items)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var i in items) p.Children.Add(i);
        return p;
    }

    /// <summary>白底的下拉選單（字型、字級等），在玄漆功能區上清楚好讀。</summary>
    public static ComboBox Combo(double width, IEnumerable<string> items, bool editable = false)
    {
        var c = new ComboBox { Width = width, Height = 26, Margin = new Thickness(1), IsEditable = editable, FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center };
        foreach (var i in items) c.Items.Add(i);
        return c;
    }

    /// <summary>Office 常見的色盤：點一格就套用。</summary>
    public static ContextMenu Palette(Action<Color?> pick, string clearLabel = "自動")
    {
        var menu = new ContextMenu();
        var grid = new UniformGrid { Columns = 10, Margin = new Thickness(4) };
        foreach (var hex in PaletteColors)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var swatch = new Button
            {
                Width = 20, Height = 20, Margin = new Thickness(2), Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)), ToolTip = hex,
            };
            swatch.Click += (_, _) => { menu.IsOpen = false; pick(color); };
            grid.Children.Add(swatch);
        }
        menu.Items.Add(new MenuItem { Header = grid, StaysOpenOnClick = true });
        var clear = new MenuItem { Header = clearLabel };
        clear.Click += (_, _) => pick(null);
        menu.Items.Add(clear);
        return menu;
    }

    public static readonly string[] PaletteColors =
    [
        "#000000", "#404040", "#7F7F7F", "#BFBFBF", "#FFFFFF", "#C00000", "#FF0000", "#FFC000", "#FFFF00", "#92D050",
        "#00B050", "#00B0F0", "#0070C0", "#002060", "#7030A0", "#C0412F", "#C8A869", "#8FB3AD", "#5E2618", "#1D1712",
    ];

    public static string ToHex(Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color? FromHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex.Equals("auto", StringComparison.OrdinalIgnoreCase)) return null;
        hex = hex.TrimStart('#');
        if (hex.Length == 8) hex = hex[2..];
        if (hex.Length != 6) return null;
        try { return Color.FromRgb(Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..], 16)); }
        catch (FormatException) { return null; }
    }
}
