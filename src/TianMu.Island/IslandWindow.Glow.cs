using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using TianMu.Core;

namespace TianMu.Island;

/// <summary>
/// 天幕本體：通知光暈。有新的異象跳出時，天幕外圍的影子化成一圈光，閃兩下再沉回陰影。
/// 顏色依異象而定（例如驗證碼描金、喝水天青、待辦朱砂），一眼就知道是哪一類。
/// </summary>
public partial class IslandWindow
{
    private static readonly Duration GlowPulse = new(TimeSpan.FromMilliseconds(320));

    /// <summary>依異象決定光暈顏色的主題鍵。</summary>
    /// <returns>null 代表不閃。</returns>
    public static string? GlowKeyFor(IslandActivity activity) => activity.GlowKey switch
    {
        null => activity.Priority >= ActivityPriority.Critical ? "ZhuShaBrush" : "MiaoJinBrush",
        "" => null,
        var key => key,
    };

    private void PlayGlow(IslandActivity activity)
    {
        if (GlowKeyFor(activity) is { } key) PlayGlow(key);
    }

    private void PlayGlow(string key)
    {
        if (!SystemParameters.ClientAreaAnimation) return; // 使用者關閉了動畫效果
        if (Capsule.Effect is not DropShadowEffect shadow || shadow.IsFrozen) return;

        var color = TryFindResource(key) is SolidColorBrush brush ? brush.Color : Color.FromRgb(0xC8, 0xA8, 0x69);

        Animate(shadow, DropShadowEffect.ColorProperty, new ColorAnimation(color, GlowPulse));
        Animate(shadow, DropShadowEffect.OpacityProperty, new DoubleAnimation(0.95, GlowPulse));
        Animate(shadow, DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(46, GlowPulse));
        Animate(shadow, DropShadowEffect.ShadowDepthProperty, new DoubleAnimation(0, GlowPulse));
    }

    private static void Animate(DropShadowEffect target, DependencyProperty property, AnimationTimeline animation)
    {
        animation.AutoReverse = true;
        animation.RepeatBehavior = new RepeatBehavior(2);
        animation.FillBehavior = FillBehavior.Stop; // 結束後回到原本的陰影
        if (animation is DoubleAnimation d) d.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
        if (animation is ColorAnimation c) c.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
        target.BeginAnimation(property, animation);
    }
}
