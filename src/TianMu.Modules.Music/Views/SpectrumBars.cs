using System.Windows;
using System.Windows.Media;

namespace TianMu.Modules.Music.Views;

/// <summary>
/// 頻譜柱：以 OnRender 直接繪製圓角長條，比用一堆 Rectangle 元件輕量得多。
/// 由外部每幀更新 <see cref="Bands"/> 後呼叫 <see cref="UIElement.InvalidateVisual"/>。
/// </summary>
public sealed class SpectrumBars : FrameworkElement
{
    public static readonly DependencyProperty BarCountProperty = DependencyProperty.Register(
        nameof(BarCount), typeof(int), typeof(SpectrumBars),
        new FrameworkPropertyMetadata(16, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CenteredProperty = DependencyProperty.Register(
        nameof(Centered), typeof(bool), typeof(SpectrumBars),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush), typeof(Brush), typeof(SpectrumBars),
        new FrameworkPropertyMetadata(DefaultBrush(), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>要畫幾根柱子（會從來源資料重新取樣）。</summary>
    public int BarCount
    {
        get => (int)GetValue(BarCountProperty);
        set => SetValue(BarCountProperty, value);
    }

    /// <summary>true：上下對稱從中線長出（收合態的小波形）；false：從底部長出。</summary>
    public bool Centered
    {
        get => (bool)GetValue(CenteredProperty);
        set => SetValue(CenteredProperty, value);
    }

    public Brush BarBrush
    {
        get => (Brush)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    /// <summary>來源頻譜資料（0~1），由法器共用同一個陣列。</summary>
    public float[]? Bands { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        int count = Math.Max(1, BarCount);
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        const double gapRatio = 0.45;
        double barWidth = w / (count + (count - 1) * gapRatio);
        double gap = barWidth * gapRatio;
        double minHeight = Math.Min(barWidth, h * 0.12);
        double radius = barWidth / 2;
        var brush = BarBrush;

        for (int i = 0; i < count; i++)
        {
            double v = Sample(i, count);
            double barHeight = Math.Max(minHeight, v * h);
            double x = i * (barWidth + gap);
            double y = Centered ? (h - barHeight) / 2 : h - barHeight;
            dc.DrawRoundedRectangle(brush, null, new Rect(x, y, barWidth, barHeight), radius, radius);
        }
    }

    private double Sample(int index, int count)
    {
        var bands = Bands;
        if (bands is null || bands.Length == 0) return 0;

        // 把來源的 N 段平均分配給 count 根柱子，每根取該區段最大值
        int start = index * bands.Length / count;
        int end = Math.Max(start + 1, (index + 1) * bands.Length / count);
        float max = 0;
        for (int i = start; i < end && i < bands.Length; i++)
            if (bands[i] > max) max = bands[i];
        return max;
    }

    private static Brush DefaultBrush()
    {
        var brush = new LinearGradientBrush(
            Color.FromRgb(0xE6, 0xCF, 0x98), Color.FromRgb(0xA8, 0x8A, 0x4E), new Point(0, 0), new Point(0, 1));
        brush.Freeze();
        return brush;
    }
}
