using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TianMu.Island.Shell;

/// <summary>
/// 天幕停在左右兩側時，法器的收合畫面整個轉 90 度排在直條裡；
/// 這裡把其中的文字改成直書：每個字轉回正立、由上往下一字一行，像書法條幅一樣直讀，不用歪著頭看。
/// 只動版面相關的屬性（繫結的文字照常更新），回到橫式時全部還原。
/// </summary>
public static class UprightText
{
    private sealed record Original(double Width, TextWrapping Wrapping, double LineHeight, LineStackingStrategy Stacking,
        TextAlignment Alignment, TextTrimming Trimming, Transform LayoutTransform, bool Clip, HorizontalAlignment HAlign);

    private static readonly DependencyProperty OriginalProperty =
        DependencyProperty.RegisterAttached("Original", typeof(Original), typeof(UprightText));

    /// <summary>容器轉了 <paramref name="containerAngle"/> 度；裡面的字反轉回來並改成一字一行。</summary>
    public static void Apply(DependencyObject root, double containerAngle)
    {
        foreach (var tb in TextBlocks(root))
        {
            if (tb.GetValue(OriginalProperty) is null)
                tb.SetValue(OriginalProperty, new Original(tb.Width, tb.TextWrapping, tb.LineHeight, tb.LineStackingStrategy,
                    tb.TextAlignment, tb.TextTrimming, tb.LayoutTransform, tb.ClipToBounds, tb.HorizontalAlignment));
            double size = tb.FontSize;
            tb.Width = Math.Ceiling(size * 1.12);     // 只容得下一個字 → 自動一字一行（英數字也會逐字斷開）
            tb.TextWrapping = TextWrapping.Wrap;
            tb.LineHeight = Math.Ceiling(size * 1.18);
            tb.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            tb.TextAlignment = TextAlignment.Center;
            tb.TextTrimming = TextTrimming.None;       // 直書放不下時由下緣裁掉，不出現橫躺的「…」
            tb.ClipToBounds = true;
            tb.LayoutTransform = new RotateTransform(-containerAngle);
            if (tb.HorizontalAlignment == HorizontalAlignment.Stretch) tb.HorizontalAlignment = HorizontalAlignment.Left;
        }
    }

    /// <summary>還原成橫書。</summary>
    public static void Restore(DependencyObject root)
    {
        foreach (var tb in TextBlocks(root))
        {
            if (tb.GetValue(OriginalProperty) is not Original o) continue;
            tb.Width = o.Width;
            tb.TextWrapping = o.Wrapping;
            tb.LineHeight = o.LineHeight;
            tb.LineStackingStrategy = o.Stacking;
            tb.TextAlignment = o.Alignment;
            tb.TextTrimming = o.Trimming;
            tb.LayoutTransform = o.LayoutTransform;
            tb.ClipToBounds = o.Clip;
            tb.HorizontalAlignment = o.HAlign;
            tb.ClearValue(OriginalProperty);
        }
    }

    /// <summary>依邏輯樹找出所有文字（畫面還沒載入也找得到）。</summary>
    private static IEnumerable<TextBlock> TextBlocks(DependencyObject root)
    {
        if (root is TextBlock self) { yield return self; yield break; }
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var tb in TextBlocks(child))
                yield return tb;
    }
}
