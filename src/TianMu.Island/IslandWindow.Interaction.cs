using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TianMu.Core;
using TianMu.Island.Interop;
using TianMu.Island.Settings;
using TianMu.Island.Shell;

namespace TianMu.Island;

/// <summary>
/// 天幕本體：互動與節奏。
///   · 智慧隱藏：閒置時縮成一條細線，滑鼠靠近才變回來
///   · 雙擊天幕：交給法器（例如開始／停止番茄鐘）
///   · 敲鐘：時間到時螢幕四邊閃光，點一下才關
///   · 彈簧手感：靈動／俐落
///   · 提示進場：新的提示由下往上彈入
/// </summary>
public partial class IslandWindow
{
    /// <summary>智慧隱藏時的細線大小。</summary>
    internal static readonly Size ThreadSize = new(64, 6);
    private static readonly TimeSpan DormantAfter = TimeSpan.FromSeconds(2.5);
    private const double WakeDistancePx = 36;

    private bool _dormant;
    private DateTime _busyAt = DateTime.Now;

    // ═══════════════════════ 智慧隱藏 ═══════════════════════

    /// <summary>有「前景」常駐異象（例如音樂、番茄鐘）時不縮成細線；背景常駐（乾坤袋）不算。</summary>
    private bool HasForegroundAmbient => _ambient.Any(a => !a.IsBackground);

    /// <summary>由感知計時器每 0.1 秒呼叫。</summary>
    private void UpdateDormant(NativeMethods.POINT cursor)
    {
        bool eligible = _settings.Read(s => s.SmartHide)
                        && !IsExpanded && _transient is null && !HasForegroundAmbient
                        && !_dragging && !_dropping && !_pressed && !_hovering;

        if (!eligible)
        {
            _busyAt = DateTime.Now;
            SetDormant(false);
            return;
        }

        bool near = CursorNearCapsule(cursor);
        if (near) _busyAt = DateTime.Now;

        if (_dormant && near) SetDormant(false);
        else if (!_dormant && DateTime.Now - _busyAt > DormantAfter) SetDormant(true);
    }

    private void SetDormant(bool dormant)
    {
        if (_dormant == dormant) return;
        _dormant = dormant;
        UpdateTargets();
    }

    /// <summary>游標是否在天幕附近（以實際像素計算；細線很薄，所以四周放寬一些）。</summary>
    private bool CursorNearCapsule(NativeMethods.POINT cursor)
    {
        if (!Capsule.IsLoaded || PresentationSource.FromVisual(Capsule) is null) return false;
        var dpi = VisualTreeHelper.GetDpi(this);
        var topLeft = Capsule.PointToScreen(new Point(0, 0));
        double w = Capsule.ActualWidth * dpi.DpiScaleX, h = Capsule.ActualHeight * dpi.DpiScaleY;
        double pad = WakeDistancePx * dpi.DpiScaleX;
        // 細線醒著時的大小差很多，以收合時的寬度為準，免得要瞄得很準
        double extra = Math.Max(0, (SmallSize.Width - ThreadSize.Width) / 2) * dpi.DpiScaleX;
        return cursor.X >= topLeft.X - pad - extra && cursor.X <= topLeft.X + w + pad + extra
            && cursor.Y >= topLeft.Y - pad - extra && cursor.Y <= topLeft.Y + h + pad + extra;
    }

    // ═══════════════════════ 雙擊 ═══════════════════════

    private Action? _doubleClickAction;
    private bool _swallowNextUp;
    private bool _expandedBeforeClick;

    public void SetDoubleClickAction(string moduleId, Action action) =>
        Dispatcher.InvokeAsync(() => _doubleClickAction = action);

    /// <summary>第二下按下時呼叫：撤銷第一下造成的展開／收起，改做法器註冊的事。</summary>
    private bool TryHandleDoubleClick(int clickCount)
    {
        if (clickCount != 2 || _doubleClickAction is null) return false;
        _userExpanded = _expandedBeforeClick;
        _swallowNextUp = true;
        Refresh();
        try { _doubleClickAction(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[天幕] 雙擊動作失敗：{ex.Message}"); }
        return true;
    }

    // ═══════════════════════ 敲鐘 ═══════════════════════

    private FocusBarrierWindow? _bell;

    public void RingBell(string message, string? toneKey = null) => Dispatcher.InvokeAsync(() =>
    {
        if (_userHidden || _fullscreenHidden) return; // 看影片、簡報時不打擾
        var color = TryFindResource(toneKey ?? "MiaoJinBrush") is SolidColorBrush b ? b.Color : Color.FromRgb(0xC8, 0xA8, 0x69);
        var screen = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _activeScreenName)
                     ?? System.Windows.Forms.Screen.PrimaryScreen;
        if (screen is null) return;

        _bell?.Close();
        _bell = FocusBarrierWindow.Bell(message, color);
        _bell.Closed += (_, _) => _bell = null;
        _bell.Cover(ToDip(screen.Bounds));
        _bell.Show();
        ReassertTopmost();
    });

    // ═══════════════════════ 圖層順序 ═══════════════════════
    //
    // 固定的上下順序（由上到下）：右鍵選單 ＞ 天幕 ＞ 敲鐘與結界光框 ＞ 其他視窗。
    // 天幕定時重新宣告置頂時，若有任何選單開著就先讓步，並把選單再拉到最上層。

    private static readonly HashSet<System.Windows.Controls.ContextMenu> _openMenus = [];

    /// <summary>App 啟動時呼叫一次：所有右鍵選單（天幕、系統匣）開啟時都會被拉到最上層。</summary>
    internal static void RegisterMenuLayering()
    {
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.ContextMenu),
            System.Windows.Controls.ContextMenu.OpenedEvent, new RoutedEventHandler((sender, _) =>
            {
                if (sender is System.Windows.Controls.ContextMenu menu)
                    _openMenus.Add(menu);
                if (sender is System.Windows.Controls.ContextMenu m)
                    m.Dispatcher.InvokeAsync(() =>
                    {
                        if (PresentationSource.FromVisual(m) is System.Windows.Interop.HwndSource source)
                            NativeMethods.ReassertTopmost(source.Handle);
                    }, System.Windows.Threading.DispatcherPriority.Loaded);
            }));
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.ContextMenu),
            System.Windows.Controls.ContextMenu.ClosedEvent, new RoutedEventHandler((sender, _) =>
            {
                if (sender is System.Windows.Controls.ContextMenu menu) _openMenus.Remove(menu);
            }));
    }

    private void ReassertTopmost()
    {
        _openMenus.RemoveWhere(m => !m.IsOpen); // 保險：沒收到關閉通知的也清掉
        if (_hwnd == 0 || _openMenus.Count > 0 || _radial is not null) return;
        NativeMethods.ReassertTopmost(_hwnd);
    }

    // ═══════════════════════ 彈簧手感 ═══════════════════════

    private void ApplySpringFeel()
    {
        bool crisp = _settings.Read(s => s.SpringFeel) == SpringFeels.Crisp;
        // 靈動 ζ ≈ 0.58（多晃一下）；俐落 ζ ≈ 0.78（很快停下）
        (_width.Stiffness, _width.Damping) = crisp ? (300.0, 27.0) : (260.0, 19.0);
        (_height.Stiffness, _height.Damping) = crisp ? (380.0, 30.4) : (340.0, 21.0);
    }

    // ═══════════════════════ 提示進場 ═══════════════════════

    /// <summary>新的提示（通知、裝置、提醒…）由下往上微微彈入、淡入。</summary>
    private void PlayEntrance()
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        if (!ReferenceEquals(PageContent.RenderTransform, _pageSlide)) PageContent.RenderTransform = _pageSlide;

        var duration = TimeSpan.FromMilliseconds(420);
        PageContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
        {
            BeginTime = TimeSpan.FromMilliseconds(90),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
        _pageSlide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, duration)
        {
            BeginTime = TimeSpan.FromMilliseconds(90),
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 },
        });
    }
}
