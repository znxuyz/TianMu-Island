using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TianMu.Island.Interop;
using TianMu.Island.Physics;
using TianMu.Island.Shell;
using Forms = System.Windows.Forms;

namespace TianMu.Island;

/// <summary>
/// 天幕本體的第五部分：感知周遭。
///   · 全螢幕隱身：看影片、玩遊戲、簡報時自動退下，結束後回來
///   · 磁吸：滑鼠靠近時，天幕微微朝游標偏移
///   · 多螢幕跟隨：滑鼠移到另一個螢幕停留一秒，天幕跟過去
///   · 晝夜變色：漆面色溫隨時辰極輕微地流轉
/// 全部共用一個每 0.1 秒的輕量計時器（只讀游標位置，幾乎不耗資源）。
/// </summary>
public partial class IslandWindow
{
    private const double MagnetRadius = 70;
    private const double MagnetMaxShift = 7;
    private static readonly TimeSpan SenseInterval = TimeSpan.FromMilliseconds(100);
    private const int FullscreenCheckEvery = 10;   // 每 1 秒檢查一次全螢幕
    private const int FollowDwellTicks = 10;       // 滑鼠在另一個螢幕停 1 秒才跟過去

    private readonly Spring _magX = new(0, stiffness: 180, damping: 17);
    private readonly Spring _magY = new(0, stiffness: 180, damping: 17);
    private readonly TranslateTransform _magnetShift = new();
    private DispatcherTimer? _senseTimer;
    private int _senseTicks;
    private bool _userHidden;
    private bool _fullscreenHidden;
    private string? _followScreenName;
    private string? _pendingScreenName;
    private int _pendingScreenTicks;
    private int _tintMinute = -1;
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

    /// <summary>使用者是否手動隱藏了天幕（系統匣「暫時隱藏」）。</summary>
    public bool UserHidden => _userHidden;

    private void InitAmbience()
    {
        Capsule.RenderTransform = _magnetShift;
        _senseTimer = new DispatcherTimer(SenseInterval, DispatcherPriority.Background, (_, _) => OnSenseTick(), Dispatcher);
        ApplyTint(force: true);
        ApplySpringFeel();
    }

    // ═══════════════════════ 顯示與隱藏 ═══════════════════════

    public void SetUserHidden(bool hidden)
    {
        _userHidden = hidden;
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        bool show = !_userHidden && !_fullscreenHidden;
        if (show && !IsVisible) Show();
        else if (!show && IsVisible) Hide();
        UpdateBarrier();
    }

    private void OnSenseTick()
    {
        _senseTicks++;
        if (_senseTicks % FullscreenCheckEvery == 0)
        {
            CheckFullscreen();
            ApplyTint(force: false);
        }

        if (!IsVisible || _dragging) return;

        NativeMethods.GetCursorPos(out var cursor);
        UpdateDormant(cursor);
        UpdateMagnet(cursor);
        FollowCursorScreen(cursor);
    }

    // ═══════════════════════ 全螢幕隱身 ═══════════════════════

    private void CheckFullscreen()
    {
        bool fullscreen = _settings.Read(s => s.AutoHideFullscreen) && IsForegroundFullscreen();
        if (fullscreen == _fullscreenHidden) return;
        _fullscreenHidden = fullscreen;
        UpdateVisibility();
    }

    /// <summary>前景視窗是否蓋滿天幕所在的螢幕（排除桌面、工作列與天幕島自己的視窗）。</summary>
    private bool IsForegroundFullscreen()
    {
        try
        {
            nint fg = NativeMethods.GetForegroundWindow();
            if (fg == 0 || _hwnd == 0) return false;

            NativeMethods.GetWindowThreadProcessId(fg, out uint pid);
            if (pid == _ownProcessId) return false;

            var name = new StringBuilder(64);
            NativeMethods.GetClassName(fg, name, name.Capacity);
            if (FullscreenRule.IsShellWindow(name.ToString())) return false;

            // 被「隱藏」的 UWP 視窗（cloaked）不算
            if (NativeMethods.DwmGetWindowAttribute(fg, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return false;

            nint islandMonitor = NativeMethods.MonitorFromWindow(_hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (NativeMethods.MonitorFromWindow(fg, NativeMethods.MONITOR_DEFAULTTONEAREST) != islandMonitor) return false;

            var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (!NativeMethods.GetMonitorInfo(islandMonitor, ref info)) return false;
            if (!NativeMethods.GetWindowRect(fg, out var rect)) return false;

            return FullscreenRule.Covers(ToRect(rect), ToRect(info.rcMonitor));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[天幕] 全螢幕偵測失敗：{ex.Message}");
            return false;
        }

        static Rect ToRect(NativeMethods.RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    // ═══════════════════════ 磁吸 ═══════════════════════

    private void UpdateMagnet(NativeMethods.POINT cursor)
    {
        Vector offset = default;
        if (!IsExpanded && !_pressed && _settings.Read(s => s.Magnetic) && SystemParameters.ClientAreaAnimation)
        {
            // 換算到「未偏移」的膠囊座標（PointFromScreen 會把目前的偏移算進去，要加回來）
            var local = Capsule.PointFromScreen(new Point(cursor.X, cursor.Y));
            local = new Point(local.X + _magnetShift.X, local.Y + _magnetShift.Y);
            offset = Magnet.Offset(local, new Rect(0, 0, Capsule.ActualWidth, Capsule.ActualHeight), MagnetRadius, MagnetMaxShift);

            // 只沿著貼住的邊滑動，不離開螢幕邊緣（嵌在角落時不動）
            var edges = CurrentEdges;
            if (edges.HasFlag(DockEdges.Top)) offset.Y = 0;
            if (edges.HasFlag(DockEdges.Left) || edges.HasFlag(DockEdges.Right)) offset.X = 0;
        }

        if (Math.Abs(_magX.Target - offset.X) < 0.05 && Math.Abs(_magY.Target - offset.Y) < 0.05) return;
        _magX.Target = offset.X;
        _magY.Target = offset.Y;
        StartAnimation();
    }

    /// <summary>由彈簧動畫迴圈每一幀呼叫。回傳是否已靜止。</summary>
    private bool StepMagnet(double dt)
    {
        _magX.Step(dt);
        _magY.Step(dt);
        _magnetShift.X = _magX.Value;
        _magnetShift.Y = _magY.Value;
        return _magX.IsAtRest && _magY.IsAtRest;
    }

    // ═══════════════════════ 多螢幕跟隨 ═══════════════════════

    private void FollowCursorScreen(NativeMethods.POINT cursor)
    {
        if (IsExpanded || !_settings.Read(s => s.FollowCursorScreen) || Forms.Screen.AllScreens.Length < 2)
        {
            _pendingScreenTicks = 0;
            return;
        }

        string name = Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y)).DeviceName;
        if (name == _activeScreenName)
        {
            _pendingScreenTicks = 0;
            return;
        }

        if (name != _pendingScreenName)
        {
            _pendingScreenName = name;
            _pendingScreenTicks = 0;
        }

        if (++_pendingScreenTicks < FollowDwellTicks) return;

        _pendingScreenTicks = 0;
        _followScreenName = name;
        ApplyDock(animate: false);
    }

    // ═══════════════════════ 晝夜變色 ═══════════════════════

    /// <summary>每分鐘依時辰更新漆面色溫（關閉時用標準玄漆）。</summary>
    private void ApplyTint(bool force)
    {
        var now = DateTime.Now;
        int minute = now.Hour * 60 + now.Minute;
        if (!force && minute == _tintMinute) return;
        _tintMinute = minute;

        if (!_settings.Read(s => s.DayNightTint))
        {
            CapsuleShape.Fill = (Brush)FindResource("LacquerBrush");
            return;
        }

        var (top, body) = DayNight.ColorsAt(now.TimeOfDay);
        var deep = Color.FromRgb((byte)(body.R * 0.72), (byte)(body.G * 0.72), (byte)(body.B * 0.72));
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops = { new GradientStop(top, 0), new GradientStop(body, 0.45), new GradientStop(deep, 1) },
        };
        brush.Freeze();
        CapsuleShape.Fill = brush;
    }

    /// <summary>設定改變時立即套用（例如關掉晝夜變色）。</summary>
    internal void OnSettingsChanged()
    {
        ApplyTint(force: true);
        ApplySpringFeel();
        if (!_settings.Read(s => s.FollowCursorScreen)) _followScreenName = null;
        CheckFullscreen();
        UpdateBarrier();
    }
}
