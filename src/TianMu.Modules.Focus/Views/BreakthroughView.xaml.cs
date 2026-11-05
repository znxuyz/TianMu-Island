using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TianMu.Modules.Focus.Views;

public partial class BreakthroughView : UserControl
{
    public BreakthroughView()
    {
        InitializeComponent();
    }

    /// <summary>落印：印章由大縮小「蓋」下去（在雷光之後）。</summary>
    public void PlayStamp()
    {
        if (Seal.RenderTransform is not ScaleTransform scale) return;

        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
        var begin = TimeSpan.FromMilliseconds(900);
        var shrink = new DoubleAnimation(1.8, 1, TimeSpan.FromMilliseconds(320)) { BeginTime = begin, EasingFunction = ease };
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { BeginTime = begin };

        Seal.Opacity = 0;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        Seal.BeginAnimation(OpacityProperty, fade);
    }
}
