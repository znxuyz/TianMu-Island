using System.Windows;
using System.Windows.Controls;

namespace TianMu.Island.Shell;

/// <summary>
/// 天幕嵌在角落時收合成正方形；法器的收合畫面原本是「圖示｜文字｜數字」橫排，
/// 這裡把它改成上下堆疊、置中，文字自動換行（最多兩行）。回到其他位置時全部還原。
/// 只動版面相關的屬性，繫結的內容照常更新。
/// </summary>
public static class SquareLayout
{
    private sealed record GridState(List<GridLength> Columns, Thickness Margin);
    private sealed record ChildState(int Column, HorizontalAlignment H, VerticalAlignment V, Thickness Margin);
    private sealed record TextState(TextWrapping Wrapping, TextAlignment Alignment, TextTrimming Trimming, double MaxHeight, HorizontalAlignment H, Thickness Margin);

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached("State", typeof(object), typeof(SquareLayout));

    public static void Apply(DependencyObject root)
    {
        if (FirstRowGrid(root) is { } grid && grid.GetValue(StateProperty) is null)
        {
            grid.SetValue(StateProperty, new GridState(grid.ColumnDefinitions.Select(c => c.Width).ToList(), grid.Margin));
            var widths = grid.ColumnDefinitions.Select(c => c.Width).ToList();
            grid.ColumnDefinitions.Clear();
            foreach (var w in widths) grid.RowDefinitions.Add(new RowDefinition { Height = w.IsStar ? GridLength.Auto : w });
            grid.Margin = new Thickness(4, 6, 4, 6);
            grid.VerticalAlignment = VerticalAlignment.Center;
            foreach (UIElement child in grid.Children)
            {
                if (child is not FrameworkElement fe) continue;
                fe.SetValue(StateProperty, new ChildState(Grid.GetColumn(fe), fe.HorizontalAlignment, fe.VerticalAlignment, fe.Margin));
                Grid.SetRow(fe, Grid.GetColumn(fe));
                Grid.SetColumn(fe, 0);
                fe.HorizontalAlignment = HorizontalAlignment.Center;
                fe.VerticalAlignment = VerticalAlignment.Center;
                fe.Margin = new Thickness(0, 2, 0, 2);
            }
        }

        foreach (var tb in TextBlocks(root))
        {
            if (tb.GetValue(StateProperty) is TextState) continue;
            tb.SetValue(StateProperty, new TextState(tb.TextWrapping, tb.TextAlignment, tb.TextTrimming, tb.MaxHeight, tb.HorizontalAlignment, tb.Margin));
            tb.TextWrapping = TextWrapping.Wrap;
            tb.TextAlignment = TextAlignment.Center;
            tb.TextTrimming = TextTrimming.CharacterEllipsis;
            tb.MaxHeight = Math.Ceiling(tb.FontSize * 1.35 * 2);   // 最多兩行
            tb.HorizontalAlignment = HorizontalAlignment.Center;
        }
    }

    public static void Restore(DependencyObject root)
    {
        if (FirstRowGrid(root, includeConverted: true) is { } grid && grid.GetValue(StateProperty) is GridState g)
        {
            grid.RowDefinitions.Clear();
            foreach (var w in g.Columns) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
            grid.Margin = g.Margin;
            grid.ClearValue(FrameworkElement.VerticalAlignmentProperty);
            foreach (UIElement child in grid.Children)
            {
                if (child is not FrameworkElement fe || fe.GetValue(StateProperty) is not ChildState c) continue;
                Grid.SetColumn(fe, c.Column);
                Grid.SetRow(fe, 0);
                fe.HorizontalAlignment = c.H;
                fe.VerticalAlignment = c.V;
                fe.Margin = c.Margin;
                fe.ClearValue(StateProperty);
            }
            grid.ClearValue(StateProperty);
        }

        foreach (var tb in TextBlocks(root))
        {
            if (tb.GetValue(StateProperty) is not TextState t) continue;
            tb.TextWrapping = t.Wrapping;
            tb.TextAlignment = t.Alignment;
            tb.TextTrimming = t.Trimming;
            tb.MaxHeight = t.MaxHeight;
            tb.HorizontalAlignment = t.H;
            tb.Margin = t.Margin;
            tb.ClearValue(StateProperty);
        }
    }

    /// <summary>找第一個「橫排多欄」的 Grid（法器收合畫面的主列）。</summary>
    private static Grid? FirstRowGrid(DependencyObject root, bool includeConverted = false)
    {
        if (root is Grid g && ((g.ColumnDefinitions.Count >= 2 && g.RowDefinitions.Count == 0) || (includeConverted && g.GetValue(StateProperty) is GridState)))
            return g;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            if (FirstRowGrid(child, includeConverted) is { } found) return found;
        return null;
    }

    private static IEnumerable<TextBlock> TextBlocks(DependencyObject root)
    {
        if (root is TextBlock self) { yield return self; yield break; }
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var tb in TextBlocks(child))
                yield return tb;
    }
}
