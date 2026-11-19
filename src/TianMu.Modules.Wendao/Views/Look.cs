using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TianMu.Modules.Wendao.Views;

/// <summary>「玄漆描金」的顏色與小元件（跟著天幕島的主題資源，找不到就用預設色）。</summary>
internal static class Look
{
    public static Brush B(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? Frozen(fallback);

    public static Brush Lacquer => B("XuanQiBrush", Color.FromRgb(0x14, 0x11, 0x0E));
    public static Brush Panel => Frozen(Color.FromRgb(0x1D, 0x18, 0x13));
    public static Brush Field => Frozen(Color.FromRgb(0x26, 0x20, 0x1A));
    public static Brush Gold => B("MiaoJinBrush", Color.FromRgb(0xC8, 0xA8, 0x69));
    public static Brush Moon => B("YueBaiBrush", Color.FromRgb(0xEC, 0xE6, 0xD8));
    public static Brush Secondary => Frozen(Color.FromArgb(0xA8, 0xEC, 0xE6, 0xD8));
    public static Brush Tertiary => Frozen(Color.FromArgb(0x70, 0xEC, 0xE6, 0xD8));
    public static Brush Hairline => Frozen(Color.FromArgb(0x22, 0xEC, 0xE6, 0xD8));
    public static Brush Cinnabar => B("ZhuShaBrush", Color.FromRgb(0xC0, 0x41, 0x2F));
    public static Brush UserBubble => Frozen(Color.FromArgb(0x30, 0xC8, 0xA8, 0x69));
    public static Brush CodeBg => Frozen(Color.FromRgb(0x0E, 0x0C, 0x0A));

    public static FontFamily Serif => Application.Current?.TryFindResource("SerifFont") as FontFamily ?? new FontFamily("serif");
    public static readonly FontFamily Sans = new("Microsoft JhengHei UI, Segoe UI");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Microsoft JhengHei UI");

    public static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    /// <summary>扁平按鈕：描金細框，滑過變亮。</summary>
    public static Button Button(string text, Action click, bool primary = false, string? tip = null)
    {
        var b = new Button
        {
            Content = text, ToolTip = tip, Cursor = System.Windows.Input.Cursors.Hand, Padding = new Thickness(12, 5, 12, 5),
            Foreground = primary ? Lacquer : Moon, Background = primary ? Gold : Brushes.Transparent,
            BorderBrush = primary ? Gold : Hairline, BorderThickness = new Thickness(1), FontFamily = Sans, FontSize = 13,
            Template = FlatTemplate(),
        };
        b.Click += (_, _) => click();
        return b;
    }

    private static ControlTemplate? _flat;

    private static ControlTemplate FlatTemplate()
    {
        if (_flat is not null) return _flat;
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Bd";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        var t = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.85));
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4));
        t.Triggers.Add(hover);
        t.Triggers.Add(disabled);
        t.Seal();
        return _flat = t;
    }

    public static TextBox Input(double fontSize = 14) => new()
    {
        Background = Field, Foreground = Moon, CaretBrush = Gold, BorderBrush = Hairline, BorderThickness = new Thickness(1),
        Padding = new Thickness(8, 6, 8, 6), FontFamily = Sans, FontSize = fontSize, SelectionBrush = Gold,
    };

    /// <summary>可選取、可複製的唯讀文字（看起來像一般文字）。</summary>
    public static TextBox ReadOnlyText(string text, FontFamily? font = null, double size = 14, Brush? fg = null) => new()
    {
        Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
        Foreground = fg ?? Moon, FontFamily = font ?? Sans, FontSize = size, Padding = new Thickness(0), SelectionBrush = Gold, IsTabStop = false,
    };
}
