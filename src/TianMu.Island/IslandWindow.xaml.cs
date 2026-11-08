using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TianMu.Core;
using TianMu.Island.Interop;
using TianMu.Island.Physics;
using TianMu.Island.Settings;
using TianMu.Island.Time;

namespace TianMu.Island;

/// <summary>
/// 天幕本體：一個無邊框、透明、置頂、不搶焦點的視窗，
/// 中間的膠囊由三組彈簧驅動（寬、高、內容顯現度），呈現果凍般的物理變形。
/// 同時它也是 <see cref="IIslandHost"/>，接收各法器送來的異象。
///
/// 顯示規則：
///   1. 有「展開型異象」（Normal 以上）→ 展開顯示它，時間到自動收回。
///   2. 使用者點擊展開 → 顯示最新的常駐異象（例如音樂面板），沒有就顯示時辰。
///   3. 收合時 → 顯示最新常駐異象的收合畫面（例如封面 + 頻譜），沒有就顯示預設的呼吸點 + 時鐘。
/// </summary>
public partial class IslandWindow : Window, IIslandHost
{
    // ── 形態尺寸（DIP）──────────────────────────────────────
    // 天幕只有三種大小，畫面永遠不會因為內容不同而忽大忽小：
    //   小：收合（預設狀態）· 中：提示（通知、驗證碼、裝置、提醒）· 大：使用者點開
    internal static readonly Size SmallSize = new(240, 34);
    internal static readonly Size MediumSize = new(420, 108);
    internal static readonly Size LargeSize = new(500, 200);
    /// <summary>嵌在角落時的收合形體：較厚，才放得下朝內的大圓弧。</summary>
    internal static readonly Size CornerSmallSize = new(212, 44);
    private const double PressScale = 0.94;
    private const double MaxCornerRadius = 34;
    private static readonly TimeSpan AutoCollapseDelay = TimeSpan.FromSeconds(3);

    // ── 彈簧：寬與高用不同頻率，才會出現「先橫拉、再縱彈」的果凍感 ──
    private readonly Spring _width = new(SmallSize.Width, stiffness: 260, damping: 19);   // ζ ≈ 0.59
    private readonly Spring _height = new(SmallSize.Height, stiffness: 340, damping: 21);  // ζ ≈ 0.57
    private readonly Spring _reveal = new(0, stiffness: 180, damping: 25);               // ζ ≈ 0.93（內容淡入不回彈）

    // ── 狀態 ──────────────────────────────────────────────
    private bool _hovering;
    private bool _pressed;
    private bool _userExpanded;
    private IslandActivity? _transient;                       // 展開型異象（同時只有一則）
    private readonly List<IslandActivity> _ambient = [];       // 常駐異象，最後一個是最新的

    private bool _animating;
    private TimeSpan _lastFrame;
    private nint _hwnd;

    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _topmostTimer;
    private readonly DispatcherTimer _transientTimer;
    private readonly DispatcherTimer _autoCollapseTimer;

    private readonly SettingsStore _settings;

    public IslandWindow(SettingsStore settings)
    {
        InitializeComponent();
        _settings = settings;
        InitHostExtensions();
        InitAmbience();
        _settings.Changed += _ => Dispatcher.InvokeAsync(OnSettingsChanged);

        _clockTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            (_, _) => UpdateClock(), Dispatcher);

        // 每 3 秒重新宣告置頂（有些程式會把天幕擠下去）；但選單開著時不搶，免得天幕蓋住選單
        _topmostTimer = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background,
            (_, _) => ReassertTopmost(), Dispatcher);

        _transientTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher);
        _transientTimer.Tick += (_, _) =>
        {
            _transientTimer.Stop();
            _transient = null;
            Refresh();
        };

        _autoCollapseTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = AutoCollapseDelay };
        _autoCollapseTimer.Tick += (_, _) =>
        {
            _autoCollapseTimer.Stop();
            if (_hovering) return;
            _userExpanded = false;
            Refresh();
        };

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) =>
        {
            ApplyDock();
            Refresh();
            ApplyVisuals();
        };
    }

    // ═══════════════════════ Win32 設定 ═══════════════════════

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;

        // 不出現在 Alt+Tab、點擊時不搶走其他視窗的焦點
        NativeMethods.ToggleExStyle(_hwnd, NativeMethods.WS_EX_TOOLWINDOW, true);
        NativeMethods.ToggleExStyle(_hwnd, NativeMethods.WS_EX_NOACTIVATE, true);

        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);

        // Ctrl + Alt + 空白鍵：隨時隱藏／叫回天幕（被別的程式佔用就算了）
        HotkeyRegistered = NativeMethods.RegisterHotKey(_hwnd, ToggleHotkeyId,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_SPACE);
    }

    private const int ToggleHotkeyId = 0x7A11;

    /// <summary>Ctrl + Alt + 空白鍵是否註冊成功。</summary>
    public bool HotkeyRegistered { get; private set; }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        // 解析度或 DPI 改變時，重新回到螢幕頂部正中央
        if (msg is NativeMethods.WM_DISPLAYCHANGE or NativeMethods.WM_DPICHANGED)
            Dispatcher.InvokeAsync(PositionAtTopCenter, DispatcherPriority.Background);

        // 滾輪（直向）與觸控板左右滑（橫向）：在天幕上換頁
        if (msg is 0x020A or 0x020E)
        {
            int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
            if (HandleWheel(msg == 0x020E ? -delta : delta)) { handled = true; return 0; }
        }

        if (msg == NativeMethods.WM_HOTKEY && wParam == ToggleHotkeyId)
        {
            Dispatcher.InvokeAsync(() => App.Current.ToggleIsland());
            handled = true;
        }
        return 0;
    }

    /// <summary>回到設定中的停靠位置（解析度、DPI 改變時呼叫）。</summary>
    private void PositionAtTopCenter() => ApplyDock();

    /// <summary>
    /// 預留：讓整個視窗完全滑鼠穿透（例如「全螢幕遊戲時隱身」模式）。
    /// </summary>
    public void SetClickThrough(bool enabled)
    {
        if (_hwnd != 0) NativeMethods.ToggleExStyle(_hwnd, NativeMethods.WS_EX_TRANSPARENT, enabled);
    }

    protected override void OnClosed(EventArgs e)
    {
        _clockTimer.Stop();
        _topmostTimer.Stop();
        _transientTimer.Stop();
        _autoCollapseTimer.Stop();
        StopAnimation();
        if (HotkeyRegistered) NativeMethods.UnregisterHotKey(_hwnd, ToggleHotkeyId);
        _barrier?.Close();
        _bell?.Close();
        base.OnClosed(e);
    }

    // ═══════════════════════ 互動 ═══════════════════════

    private void Capsule_MouseEnter(object sender, MouseEventArgs e)
    {
        // 大小固定，滑過時只讓頂端的描金線亮起來
        _hovering = true;
        TopHairline.Opacity = 1;
        _autoCollapseTimer.Stop();
        UpdateTargets();
    }

    private void Capsule_MouseLeave(object sender, MouseEventArgs e)
    {
        _hovering = false;
        TopHairline.Opacity = 0.7;
        _pressed = false;

        // 使用者展開後滑鼠離開，過幾秒自動收回（像靈動島一樣）
        if (_userExpanded)
        {
            _autoCollapseTimer.Stop();
            _autoCollapseTimer.Start();
        }

        UpdateTargets();
    }

    private void Capsule_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TryHandleDoubleClick(e.ClickCount))
        {
            e.Handled = true;
            return;
        }

        _pressed = true;
        BeginPress();
        StartLongPress();
        Capsule.CaptureMouse();
        UpdateTargets();
        e.Handled = true;
    }

    private void Capsule_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        bool wasPressed = _pressed;
        _pressed = false;
        Capsule.ReleaseMouseCapture();

        CancelLongPress();
        if (ReleaseRadial())
        {
            // 長按叫出了法陣盤：這次放開由它處理，不算點擊
            Refresh();
            e.Handled = true;
            return;
        }

        if (_swallowNextUp)
        {
            // 雙擊的第二下：已經交給法器處理了
            _swallowNextUp = false;
            e.Handled = true;
            return;
        }
        _expandedBeforeClick = _userExpanded;

        // 剛才是拖曳：吸附到停靠位置，不當作點擊
        if (EndPressAsDrag())
        {
            Refresh();
            e.Handled = true;
            return;
        }

        if (wasPressed)
        {
            if (IsExpanded)
            {
                // 收起：同時撤掉目前的展開型異象
                _userExpanded = false;
                _transient = null;
                _transientTimer.Stop();
            }
            else
            {
                _userExpanded = true;
            }

            // 放開瞬間給高度一記推力，增加「啵」一下的彈性
            _height.Impulse(IsExpanded ? 240 : -160);
        }

        Refresh();
        e.Handled = true;
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => App.Current.ShowSettings();
    private void Hide_Click(object sender, RoutedEventArgs e) => App.Current.ToggleIsland();
    private void Exit_Click(object sender, RoutedEventArgs e) => App.Current.Quit();

    // ═══════════════════════ 法器宿主 IIslandHost ═══════════════════════

    public void Post(IslandActivity activity)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (activity.Priority == ActivityPriority.Ambient)
            {
                bool isNew = _ambient.RemoveAll(a => a.ModuleId == activity.ModuleId) == 0;
                _ambient.Add(activity);
                // 新出現的（例如開始播歌）優先顯示；背景常駐（乾坤袋）不搶
                if (isNew && activity.ExpandedView is not null && !activity.IsBackground) SelectPage(activity.ModuleId);
            }
            else
            {
                if (_transient is not null && activity.Priority < _transient.Priority)
                    return; // 低階異象不能壓過高階異象

                bool replaced = _transient is not null;
                _transient = activity;
                PlayGlow(activity);
                if (!replaced || !_userExpanded) Dispatcher.InvokeAsync(PlayEntrance, DispatcherPriority.Loaded);
                _transientTimer.Stop();
                if (activity.Duration is { } duration)
                {
                    _transientTimer.Interval = duration;
                    _transientTimer.Start();
                }
            }

            Refresh();
        });
    }

    public void Dismiss(string moduleId)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _ambient.RemoveAll(a => a.ModuleId == moduleId);
            if (_transient?.ModuleId == moduleId)
            {
                _transient = null;
                _transientTimer.Stop();
            }
            Refresh();
        });
    }

    public Task InvokeAsync(Action action) => Dispatcher.InvokeAsync(action).Task;

    public IModuleSettings GetModuleSettings(string moduleId) => _settings.ForModule(moduleId);

    // ═══════════════════════ 內容切換 ═══════════════════════

    private bool IsExpanded => _userExpanded || _transient is not null;

    internal enum SizeClass { Small, Medium, Large }

    /// <summary>目前該用哪一種大小：使用者點開一律是大；只是跳出提示是中；其餘是小。</summary>
    private SizeClass CurrentSizeClass =>
        _userExpanded ? SizeClass.Large
        : _transient is not null || _dropping ? SizeClass.Medium
        : SizeClass.Small;

    /// <summary>收合時佔據膠囊的常駐異象：最新的前景常駐，沒有才輪到背景常駐（例如乾坤袋）。</summary>
    private IslandActivity? CurrentAmbient => _ambient.LastOrDefault(a => !a.IsBackground) ?? _ambient.LastOrDefault();

    /// <summary>目前展開時要顯示的異象。</summary>
    private IslandActivity? ExpandedActivity => _transient ?? CurrentPage;

    /// <summary>依目前狀態決定兩個層各顯示什麼，然後更新彈簧目標。</summary>
    private void Refresh()
    {
        // 收合層
        var compactView = CurrentAmbient?.CompactView as UIElement;
        SetHostContent(CompactHost, compactView);
        UpdateCompactOrientation(hasModuleView: compactView is not null);

        // 展開層
        var expanded = ExpandedActivity;
        var expandedView = expanded?.ExpandedView as UIElement;
        SetHostContent(ExpandedHost, expandedView);
        DefaultExpanded.Visibility = expandedView is null ? Visibility.Visible : Visibility.Collapsed;

        UpdatePager();
        UpdateSatellite();
        UpdateIndicators();
        UpdateClock();
        UpdateTargets();
    }

    private static void SetHostContent(System.Windows.Controls.ContentControl host, UIElement? view)
    {
        if (ReferenceEquals(host.Content, view)) return;
        host.Content = view;
        host.Visibility = view is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // ═══════════════════════ 時辰 ═══════════════════════

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        VerticalClockText.Text = now.ToString("HH\nmm", CultureInfo.InvariantCulture);

        var shown = ExpandedActivity;
        if (shown is null || shown.ExpandedView is not null)
        {
            // 沒有異象（或異象自帶畫面）時，預設面板顯示時辰：大字時間、農曆、直書時辰
            TitleText.FontSize = 38;
            TitleText.Text = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            string? year = Lunar.GanZhiYear(now);
            string? monthDay = Lunar.MonthDay(now);
            SubtitleText.Text = year is null || monthDay is null ? string.Empty : $"{year}年 · {monthDay}";
            ShichenText.Text = $"{Lunar.Shichen(now)}\n時";
            ShichenText.Visibility = Visibility.Visible;
            UpdateQuickBarVisibility(showingClock: true);
        }
        else
        {
            TitleText.FontSize = 24;
            TitleText.Text = shown.Title;
            SubtitleText.Text = shown.Subtitle ?? string.Empty;
            ShichenText.Visibility = Visibility.Collapsed;
            UpdateQuickBarVisibility(showingClock: false);
        }
    }

    // ═══════════════════════ 彈簧動畫核心 ═══════════════════════

    private void UpdateTargets()
    {
        Size target = CurrentSizeClass switch
        {
            SizeClass.Small when _dormant => IsVertical ? new Size(ThreadSize.Height, ThreadSize.Width) : ThreadSize,
            SizeClass.Large => LargeSize,
            SizeClass.Medium => MediumSize,
            // 收合：停在左右側時轉成直式；嵌在角落時用較厚的角落形體
            _ when IsVertical => new Size(SmallSize.Height, SmallSize.Width),
            _ when IsCorner => CornerSmallSize,
            _ => SmallSize,
        };

        double scale = _pressed ? PressScale : 1.0;

        _width.Target = target.Width * scale;
        _height.Target = target.Height * scale;
        _reveal.Target = IsExpanded ? 1 : 0;

        StartAnimation();
    }

    private void StartAnimation()
    {
        if (_animating) return;
        _animating = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopAnimation()
    {
        if (!_animating) return;
        _animating = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (_lastFrame == TimeSpan.Zero)
        {
            _lastFrame = now;
            return;
        }

        double dt = (now - _lastFrame).TotalSeconds;
        if (dt <= 0) return; // 同一幀可能被呼叫多次
        _lastFrame = now;
        dt = Math.Min(dt, 1.0 / 30); // 卡頓時避免一次跳太遠

        _width.Step(dt);
        _height.Step(dt);
        _reveal.Step(dt);
        bool magnetAtRest = StepMagnet(dt);
        bool gooAtRest = StepGoo(dt);

        ApplyVisuals();

        if (_width.IsAtRest && _height.IsAtRest && _reveal.IsAtRest && magnetAtRest && gooAtRest)
            StopAnimation(); // 靜止時不耗任何 CPU/GPU
    }

    private void ApplyVisuals()
    {
        double w = Math.Max(2, _width.Value);
        double h = Math.Max(2, _height.Value);
        double radius = Math.Min(Math.Min(w, h) / 2, MaxCornerRadius);

        Capsule.Width = w;
        Capsule.Height = h;

        // 畫出形體（貼邊瀏海或圓膠囊），內容沿著外形裁切，變形時邊緣依然乾淨
        ApplyShape(w, h, radius);

        double p = Math.Clamp(_reveal.Value, 0, 1);
        double idleOpacity = Math.Clamp(1 - p * 2.2, 0, 1);          // 收合內容先快速隱去
        idleOpacity *= Math.Clamp((Math.Min(w, h) - 12) / 14, 0, 1);  // 縮成細線（智慧隱藏）時內容跟著淡去
        double expandedOpacity = Math.Clamp((p - 0.35) / 0.65, 0, 1); // 形體撐開後內容才浮現

        SetLayer(IdleLayer, idleOpacity, interactive: p < 0.5);
        IndicatorPanel.Opacity = Math.Clamp((Math.Min(w, h) - 12) / 14, 0, 1);
        SetLayer(ExpandedLayer, expandedOpacity, interactive: p >= 0.5);
    }

    private static void SetLayer(UIElement layer, double opacity, bool interactive)
    {
        layer.Opacity = opacity;
        layer.Visibility = opacity > 0 ? Visibility.Visible : Visibility.Hidden;
        layer.IsHitTestVisible = interactive;
    }
}
