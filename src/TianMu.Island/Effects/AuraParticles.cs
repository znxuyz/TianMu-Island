using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace TianMu.Island.Effects;

/// <summary>
/// 靈氣粒子：細小金塵從膠囊兩端緩緩飄散、淡出。
///
/// 省電設計：
///   · 每顆粒子的動畫限制在每秒 30 格（不是螢幕更新率）
///   · <see cref="Stop"/> 之後不再產生新粒子，現有的自然淡出後就完全不耗資源
/// </summary>
internal sealed class AuraParticles
{
    private const int FrameRate = 30;

    private readonly Canvas _layer;
    private readonly DispatcherTimer _emitter;
    private readonly Random _random = new();
    private Func<(Point Left, Point Right, double Height)>? _anchor;

    public AuraParticles(Canvas layer)
    {
        _layer = layer;
        _emitter = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(650) };
        _emitter.Tick += (_, _) => Emit();
    }

    /// <summary>同時存在的粒子上限（隨境界增加）。</summary>
    public int MaxAlive { get; set; } = 5;

    /// <summary>粒子亮度（0~1）。</summary>
    public double Brightness { get; set; } = 0.75;

    public void Run(Func<(Point Left, Point Right, double Height)> anchor)
    {
        _anchor = anchor;
        if (!_emitter.IsEnabled) _emitter.Start();
    }

    public void Stop()
    {
        if (_emitter.IsEnabled) _emitter.Stop();
    }

    private void Emit()
    {
        if (_anchor is null || _layer.Children.Count >= MaxAlive) return;
        if (_random.NextDouble() < 0.35) return; // 稍微不規律，比較像自然飄散

        var (left, right, height) = _anchor();
        bool fromLeft = _random.NextDouble() < 0.5;
        var origin = fromLeft ? left : right;
        double direction = fromLeft ? -1 : 1;

        double size = 1.6 + _random.NextDouble() * 1.8;
        var dot = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = GoldDust,
            Opacity = 0,
            IsHitTestVisible = false,
        };

        double x0 = origin.X + direction * _random.NextDouble() * 4 - size / 2;
        double y0 = origin.Y + (_random.NextDouble() - 0.5) * height * 0.6 - size / 2;
        Canvas.SetLeft(dot, x0);
        Canvas.SetTop(dot, y0);
        _layer.Children.Add(dot);

        var duration = TimeSpan.FromSeconds(3.5 + _random.NextDouble() * 2);
        double dx = direction * (16 + _random.NextDouble() * 30);
        double dy = (_random.NextDouble() - 0.4) * 22;
        var ease = new SineEase { EasingMode = EasingMode.EaseOut };

        var moveX = new DoubleAnimation(x0, x0 + dx, duration) { EasingFunction = ease };
        var moveY = new DoubleAnimation(y0, y0 + dy, duration) { EasingFunction = ease };
        var glow = new DoubleAnimationUsingKeyFrames { Duration = duration };
        glow.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        glow.KeyFrames.Add(new SplineDoubleKeyFrame(0.9 * Brightness, KeyTime.FromPercent(0.2)));
        glow.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromPercent(1)));

        foreach (Timeline t in new Timeline[] { moveX, moveY, glow })
            Timeline.SetDesiredFrameRate(t, FrameRate);

        glow.Completed += (_, _) => _layer.Children.Remove(dot);

        dot.BeginAnimation(Canvas.LeftProperty, moveX);
        dot.BeginAnimation(Canvas.TopProperty, moveY);
        dot.BeginAnimation(UIElement.OpacityProperty, glow);
    }

    private static readonly Brush GoldDust = CreateDust();

    private static Brush CreateDust()
    {
        var brush = new RadialGradientBrush(Color.FromArgb(0xFF, 0xF3, 0xDF, 0xA8), Color.FromArgb(0x00, 0xC8, 0xA8, 0x69));
        brush.Freeze();
        return brush;
    }
}
