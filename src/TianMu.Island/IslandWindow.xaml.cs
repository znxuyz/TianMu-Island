using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TianMu.Core;
using TianMu.Island.Interop;
using TianMu.Island.Physics;

namespace TianMu.Island;

/// <summary>
/// 天幕本體：一個無邊框、透明、置頂、不搶焦點的視窗，
/// 中間的膠囊由三組彈簧驅動（寬、高、內容顯現度），呈現果凍般的物理變形。
/// 同時它也是 <see cref="IIslandHost"/>，接收各法器送來的異象。
/// </summary>
public partial class IslandWindow : Window, IIslandHost
{
    // ── 形態尺寸（DIP）──────────────────────────────────────
    private static readonly Size IdleSize = new(180, 34);
    private static readonly Size HoverSize = new(204, 38);
    private static readonly Size ExpandedSize = new(400, 136);
    private const double PressScale = 0.94;
    private const double MaxCornerRadius = 34;

    // ── 彈簧：寬與高用不同頻率，才會出現「先橫拉、再縱彈」的果凍感 ──
    private readonly Spring _width = new(IdleSize.Width, stiffness: 260, damping: 19);   // ζ ≈ 0.59
    private readonly Spring _height = new(IdleSize.Height, stiffness: 340, damping: 21);  // ζ ≈ 0.57
    private readonly Spring _reveal = new(0, stiffness: 180, damping: 25);               // ζ ≈ 0.93（內容淡入不回彈）

    // ── 狀態 ──────────────────────────────────────────────
    private bool _hovering;
    private bool _pressed;
    private bool _userExpanded;
    private IslandActivity? _activity;

    private bool _animating;
    private TimeSpan _lastFrame;
    private nint _hwnd;

    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _topmostTimer;
    private readonly DispatcherTimer _activityTimer;

    private static readonly string[] Stems = ["甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸"];
    private static readonly string[] Branches = ["子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥"];
    private static readonly ChineseLunisolarCalendar Lunar = new();

    public IslandWindow()
    {
        InitializeComponent();

        _clockTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            (_, _) => UpdateClock(), Dispatcher);

        _topmostTimer = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background,
            (_, _) => { if (_hwnd != 0) NativeMethods.ReassertTopmost(_hwnd); }, Dispatcher);

        _activityTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher);
        _activityTimer.Tick += (_, _) =>
        {
            _activityTimer.Stop();
            _activity = null;
            UpdateTargets();
        };

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) =>
        {
            PositionAtTopCenter();
            UpdateClock();
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
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        // 解析度或 DPI 改變時，重新回到螢幕頂部正中央
        if (msg is NativeMethods.WM_DISPLAYCHANGE or NativeMethods.WM_DPICHANGED)
            Dispatcher.InvokeAsync(PositionAtTopCenter, DispatcherPriority.Background);
        return 0;
    }

    private void PositionAtTopCenter()
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
    }

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
        _activityTimer.Stop();
        StopAnimation();
        base.OnClosed(e);
    }

    // ═══════════════════════ 互動 ═══════════════════════

    private void Capsule_MouseEnter(object sender, MouseEventArgs e)
    {
        _hovering = true;
        UpdateTargets();
    }

    private void Capsule_MouseLeave(object sender, MouseEventArgs e)
    {
        _hovering = false;
        _pressed = false;
        UpdateTargets();
    }

    private void Capsule_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressed = true;
        Capsule.CaptureMouse();
        UpdateTargets();
        e.Handled = true;
    }

    private void Capsule_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        bool wasPressed = _pressed;
        _pressed = false;
        Capsule.ReleaseMouseCapture();

        if (wasPressed)
        {
            if (IsExpanded)
            {
                // 收起：同時撤掉目前的異象
                _userExpanded = false;
                _activity = null;
                _activityTimer.Stop();
            }
            else
            {
                _userExpanded = true;
            }

            // 放開瞬間給高度一記向上的推力，增加「啵」一下的彈性
            _height.Impulse(IsExpanded ? 240 : -160);
        }

        UpdateTargets();
        e.Handled = true;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    // ═══════════════════════ 法器宿主 IIslandHost ═══════════════════════

    public void Post(IslandActivity activity)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_activity is not null && activity.Priority < _activity.Priority)
                return; // 低階異象不能壓過高階異象

            _activity = activity;
            TitleText.Text = activity.Title;
            SubtitleText.Text = activity.Subtitle ?? string.Empty;

            _activityTimer.Stop();
            if (activity.Duration is { } duration)
            {
                _activityTimer.Interval = duration;
                _activityTimer.Start();
            }

            UpdateTargets();
        });
    }

    public void Dismiss(string moduleId)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_activity?.ModuleId != moduleId) return;
            _activity = null;
            _activityTimer.Stop();
            UpdateTargets();
        });
    }

    // ═══════════════════════ 時辰 ═══════════════════════

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("HH:mm", CultureInfo.InvariantCulture);

        if (_activity is null)
        {
            TitleText.Text = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            SubtitleText.Text = $"{GanZhiYear(now)}年 · {Shichen(now)}時";
        }
    }

    private static string GanZhiYear(DateTime date)
    {
        try
        {
            int year = Lunar.GetSexagenaryYear(date);
            return Stems[Lunar.GetCelestialStem(year) - 1] + Branches[Lunar.GetTerrestrialBranch(year) - 1];
        }
        catch (ArgumentOutOfRangeException)
        {
            return "天幕";
        }
    }

    // 子時 = 23:00–00:59，丑時 = 01:00–02:59 ……
    private static string Shichen(DateTime date) => Branches[(date.Hour + 1) / 2 % 12];

    // ═══════════════════════ 彈簧動畫核心 ═══════════════════════

    private bool IsExpanded => _userExpanded || _activity is not null;

    private void UpdateTargets()
    {
        Size target = IsExpanded ? ExpandedSize : _hovering ? HoverSize : IdleSize;
        double scale = _pressed ? PressScale : 1.0;

        _width.Target = target.Width * scale;
        _height.Target = target.Height * scale;
        _reveal.Target = IsExpanded ? 1 : 0;

        UpdateClock(); // 異象結束時，標題立刻回到時辰顯示

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

        ApplyVisuals();

        if (_width.IsAtRest && _height.IsAtRest && _reveal.IsAtRest)
            StopAnimation(); // 靜止時不耗任何 CPU/GPU
    }

    private void ApplyVisuals()
    {
        double w = Math.Max(1, _width.Value);
        double h = Math.Max(1, _height.Value);

        Capsule.Width = w;
        Capsule.Height = h;
        Capsule.CornerRadius = new CornerRadius(Math.Min(h / 2, MaxCornerRadius));

        double p = Math.Clamp(_reveal.Value, 0, 1);
        IdleLayer.Opacity = Math.Clamp(1 - p * 2.2, 0, 1);   // 靜息內容先快速隱去
        ExpandedLayer.Opacity = Math.Clamp((p - 0.35) / 0.65, 0, 1); // 形體撐開後內容才浮現
    }
}
