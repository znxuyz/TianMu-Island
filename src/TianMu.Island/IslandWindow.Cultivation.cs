using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using TianMu.Core;
using TianMu.Island.Effects;

namespace TianMu.Island;

/// <summary>
/// 天幕本體的第二部分：修為外觀、渡劫特效、靈氣粒子、捷徑列、專注模式與法器資料夾。
/// </summary>
public partial class IslandWindow
{
    private static readonly TimeSpan ParticleIdleDelay = TimeSpan.FromSeconds(60);

    private IslandRealm _realm = IslandRealm.ZhuJi;     // 尚未有修為紀錄時，以築基外觀示人
    private readonly List<QuickAction> _quickActions = [];
    private readonly Dictionary<string, string> _statuses = [];
    private string? _latestStatusModule;
    private volatile bool _focusMode;
    private DispatcherTimer? _idleTimer;
    private TimeSpan _idleFor;
    private AuraParticles? _particles;

    public event Action<bool>? FocusModeChanged;

    public bool IsFocusMode => _focusMode;

    private void InitHostExtensions()
    {
        _particles = new AuraParticles(ParticleLayer);
        _idleTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => OnIdleTick(), Dispatcher);
        ApplyRealmVisuals();
    }

    // ═══════════════════════ 法器資料夾 ═══════════════════════

    private static readonly Regex SafeModuleId = new("^[A-Za-z0-9._-]{1,64}$", RegexOptions.Compiled);

    public string GetDataDirectory(string moduleId)
    {
        if (!SafeModuleId.IsMatch(moduleId) || moduleId.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException($"法器 Id 只能包含英數字與 . _ -：{moduleId}", nameof(moduleId));

        string dir = System.IO.Path.Combine(_settings.Directory, "data", moduleId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ═══════════════════════ 修為外觀 ═══════════════════════

    public void SetRealm(IslandRealm realm) => Dispatcher.InvokeAsync(() =>
    {
        _realm = realm;
        ApplyRealmVisuals();
    });

    /// <summary>每升一境，天幕多一分細節。</summary>
    private void ApplyRealmVisuals()
    {
        bool golden = _realm >= IslandRealm.ZhuJi;
        SpiritDot.Fill = golden
            ? (Brush)FindResource("MiaoJinBrush")
            : (Brush)FindResource("TextSecondaryBrush");
        if (SpiritDot.Effect is System.Windows.Media.Effects.DropShadowEffect glow)
            glow.Opacity = golden ? 0.85 : 0.2;

        CoreGlow.Visibility = _realm >= IslandRealm.JinDan ? Visibility.Visible : Visibility.Collapsed;
        CoreGlow.Opacity = _realm >= IslandRealm.YuanYing ? 1.0 : 0.7;

        bool huaShen = _realm >= IslandRealm.HuaShen;
        Capsule.BorderThickness = new Thickness(huaShen ? 1 : 0);
        Capsule.BorderBrush = huaShen ? new SolidColorBrush(Color.FromArgb(0x73, 0xC8, 0xA8, 0x69)) : null;
        IdleSeal.Visibility = huaShen ? Visibility.Visible : Visibility.Collapsed;

        if (_particles is not null)
        {
            _particles.MaxAlive = _realm switch
            {
                IslandRealm.LianQi => 3,
                IslandRealm.ZhuJi => 5,
                IslandRealm.JinDan => 8,
                IslandRealm.YuanYing => 12,
                _ => 16,
            };
            _particles.Brightness = _realm >= IslandRealm.YuanYing ? 1.0 : 0.75;
        }
    }

    // ═══════════════════════ 渡劫 ═══════════════════════

    public void PlayTribulation() => Dispatcher.InvokeAsync(PlayTribulationCore);

    /// <summary>三道雷光，接著一條金線沿膠囊邊緣走完一圈。</summary>
    private void PlayTribulationCore()
    {
        bool motion = SystemParameters.ClientAreaAnimation;

        var flash = new DoubleAnimationUsingKeyFrames();
        void Key(double seconds, double value) =>
            flash.KeyFrames.Add(new LinearDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds))));
        Key(0, 0);
        Key(0.05, 0.75);
        Key(0.2, 0);
        if (motion)
        {
            Key(0.34, 0.5);
            Key(0.44, 0);
            Key(0.62, 0.9);
            Key(0.85, 0);
        }
        FlashLayer.BeginAnimation(OpacityProperty, flash);

        if (!motion) return;

        // 金線：以展開後的目標尺寸繞行（此時膠囊正在彈開）
        double w = _width.Target, h = _height.Target;
        double r = Math.Min(h / 2, MaxCornerRadius);
        double thickness = EdgeRun.StrokeThickness;
        double perimeter = 2 * (w + h) - (8 - 2 * Math.PI) * r;
        double dash = perimeter / thickness;

        EdgeRun.Width = w;
        EdgeRun.Height = h;
        EdgeRun.RadiusX = r;
        EdgeRun.RadiusY = r;
        EdgeRun.StrokeDashArray = [dash, dash];
        EdgeRun.Opacity = 1;
        EdgeRun.Visibility = Visibility.Visible;

        var run = new DoubleAnimation(dash, 0, TimeSpan.FromSeconds(1.1))
        {
            BeginTime = TimeSpan.FromSeconds(0.45),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.5)) { BeginTime = TimeSpan.FromSeconds(1.65) };
        fade.Completed += (_, _) => EdgeRun.Visibility = Visibility.Collapsed;

        EdgeRun.StrokeDashOffset = dash;
        EdgeRun.BeginAnimation(Shape.StrokeDashOffsetProperty, run);
        EdgeRun.BeginAnimation(OpacityProperty, fade);
    }

    // ═══════════════════════ 捷徑列與狀態 ═══════════════════════

    public void AddQuickAction(QuickAction action) => Dispatcher.InvokeAsync(() =>
    {
        _quickActions.RemoveAll(a => a.ModuleId == action.ModuleId && a.Label == action.Label);
        _quickActions.Add(action);
        RebuildQuickButtons();
        Refresh();
    });

    public void SetStatus(string moduleId, string? text) => Dispatcher.InvokeAsync(() =>
    {
        if (string.IsNullOrWhiteSpace(text)) _statuses.Remove(moduleId);
        else
        {
            _statuses[moduleId] = text;
            _latestStatusModule = moduleId;
        }

        StatusText.Text = _latestStatusModule is not null && _statuses.TryGetValue(_latestStatusModule, out var s)
            ? s
            : _statuses.Values.LastOrDefault() ?? string.Empty;
        Refresh();
    });

    private bool HasQuickBar => _quickActions.Count > 0 || _statuses.Count > 0;

    /// <summary>目前所有捷徑（系統匣選單也會用到）。只在 UI 執行緒讀取。</summary>
    public IReadOnlyList<QuickAction> QuickActions => _quickActions.OrderBy(a => a.Order).ToList();

    private const string QuickMenuTag = "quick";

    /// <summary>右鍵選單開啟時，把法器捷徑放在最上方（聽音樂時展開面板看不到捷徑列）。</summary>
    private void CapsuleMenu_Opened(object sender, RoutedEventArgs e)
    {
        foreach (var old in CapsuleMenu.Items.OfType<FrameworkElement>().Where(i => QuickMenuTag.Equals(i.Tag)).ToList())
            CapsuleMenu.Items.Remove(old);

        var actions = QuickActions;
        if (actions.Count == 0) return;

        int index = 0;
        foreach (var action in actions)
        {
            var item = new MenuItem
            {
                Header = action.Label,
                Tag = QuickMenuTag,
                Style = (Style)FindResource("TianMu.MenuItem.Accent"),
            };
            item.Click += (_, _) => action.Invoke();
            CapsuleMenu.Items.Insert(index++, item);
        }
        CapsuleMenu.Items.Insert(index, new Separator { Tag = QuickMenuTag, Style = (Style)FindResource("TianMu.Separator") });
    }

    private void RebuildQuickButtons()
    {
        QuickButtons.Children.Clear();
        var style = (Style)FindResource("TianMu.Button.Ghost");
        foreach (var action in _quickActions.OrderBy(a => a.Order))
        {
            var button = new Button
            {
                Content = action.Label,
                Style = style,
                MinHeight = 30,
                Padding = new Thickness(16, 0, 16, 0),
                Margin = new Thickness(8, 0, 0, 0),
                Focusable = false,
            };
            button.Click += (_, _) => action.Invoke();
            QuickButtons.Children.Add(button);
        }
    }

    /// <summary>捷徑列只在「時辰」面板顯示（顯示通知文字時不出現）。</summary>
    private void UpdateQuickBarVisibility(bool showingClock) =>
        QuickBar.Visibility = showingClock && HasQuickBar ? Visibility.Visible : Visibility.Collapsed;

    // ═══════════════════════ 專注模式 ═══════════════════════

    public void SetFocusMode(bool enabled) => Dispatcher.InvokeAsync(() =>
    {
        if (_focusMode == enabled) return;
        _focusMode = enabled;
        FocusModeChanged?.Invoke(enabled);
    });

    // ═══════════════════════ 靈氣粒子 ═══════════════════════

    /// <summary>天幕完全閒置（收合、沒有任何異象、滑鼠不在上面）滿一分鐘後，金塵開始飄散。</summary>
    private void OnIdleTick()
    {
        if (_particles is null) return;

        bool idle = IsVisible && !IsExpanded && !_hovering && _ambient.Count == 0 && _transient is null;
        bool allowed = _settings.Read(s => s.ShowParticles) && SystemParameters.ClientAreaAnimation;

        _idleFor = idle && allowed ? _idleFor + TimeSpan.FromSeconds(1) : TimeSpan.Zero;

        if (_idleFor >= ParticleIdleDelay) _particles.Run(CapsuleEnds);
        else _particles.Stop();
    }

    /// <summary>膠囊兩端（粒子出生處），以粒子層的座標表示。直式時是上下兩端。</summary>
    private (Point Left, Point Right, double Height) CapsuleEnds()
    {
        double w = Capsule.ActualWidth, h = Capsule.ActualHeight;
        if (h > w)
        {
            var upper = Capsule.TranslatePoint(new Point(w / 2, w * 0.3), ParticleLayer);
            var lower = Capsule.TranslatePoint(new Point(w / 2, h - w * 0.3), ParticleLayer);
            return (upper, lower, w);
        }
        var left = Capsule.TranslatePoint(new Point(h * 0.3, h / 2), ParticleLayer);
        var right = Capsule.TranslatePoint(new Point(w - h * 0.3, h / 2), ParticleLayer);
        return (left, right, h);
    }
}
