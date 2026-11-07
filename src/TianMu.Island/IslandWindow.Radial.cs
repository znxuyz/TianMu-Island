using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TianMu.Core;
using TianMu.Island.Interop;
using TianMu.Island.Shell;
using Forms = System.Windows.Forms;

namespace TianMu.Island;

/// <summary>
/// 天幕本體：法陣盤。在天幕上長按半秒，游標處展開圓形功能選單（金線輪轉）。
/// 扇區由各法器透過 <see cref="AddRadialAction"/> 提供，天幕自己補上「隱藏」與「設定」。
/// </summary>
public partial class IslandWindow
{
    private static readonly TimeSpan LongPressDelay = TimeSpan.FromMilliseconds(500);
    private const string FurnaceLabel = "化形爐";

    private readonly List<RadialAction> _radialActions = [];
    private DispatcherTimer? _longPress;
    private RadialMenuWindow? _radial;
    private bool _radialFromPress;

    public void AddRadialAction(RadialAction action) => Dispatcher.InvokeAsync(() =>
    {
        _radialActions.RemoveAll(a => a.ModuleId == action.ModuleId && a.Label == action.Label);
        _radialActions.Add(action);
    });

    public bool ShowPage(string moduleId)
    {
        if (!Dispatcher.CheckAccess()) return Dispatcher.Invoke(() => ShowPage(moduleId));
        if (Pages().All(p => p.Key != moduleId)) return false;
        SelectPage(moduleId);
        _userExpanded = true;
        _autoCollapseTimer.Stop();
        Refresh();
        return true;
    }

    // ═══════════════════════ 長按 ═══════════════════════

    private void StartLongPress()
    {
        _longPress ??= new DispatcherTimer(LongPressDelay, DispatcherPriority.Input, (_, _) => OnLongPress(), Dispatcher);
        _longPress.Stop();
        _longPress.Start();
    }

    private void CancelLongPress() => _longPress?.Stop();

    private void OnLongPress()
    {
        _longPress?.Stop();
        if (!_pressed || _dragging || _radial is not null) return;

        _pressed = false;      // 不再算點擊，也不再縮小
        _radialFromPress = true;
        UpdateTargets();
        OpenRadial();
    }

    private void OpenRadial()
    {
        NativeMethods.GetCursorPos(out var cursor);
        var screen = Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y));
        var dpi = VisualTreeHelper.GetDpi(this);
        var center = new Point(cursor.X / dpi.DpiScaleX, cursor.Y / dpi.DpiScaleY);

        _radial = new RadialMenuWindow(RadialItems());
        _radial.Closed += (_, _) =>
        {
            _radial = null;
            _radialFromPress = false;
            ReassertTopmost();
        };
        _radial.ShowAt(center, ToDip(screen.WorkingArea));
    }

    /// <summary>按住拖曳時，把游標位置轉給法陣盤。回傳是否由法陣盤處理。</summary>
    private bool TrackRadial()
    {
        if (_radial is null || !_radialFromPress) return false;
        NativeMethods.GetCursorPos(out var c);
        _radial.Track(new Point(c.X, c.Y));
        return true;
    }

    /// <summary>放開滑鼠時：交給法陣盤決定執行、取消或改用點的。回傳是否由法陣盤處理。</summary>
    private bool ReleaseRadial()
    {
        if (_radial is null || !_radialFromPress) return false;
        _radialFromPress = false;
        NativeMethods.GetCursorPos(out var c);
        _radial.Release(new Point(c.X, c.Y));
        return true;
    }

    // ═══════════════════════ 扇區 ═══════════════════════

    private IReadOnlyList<RadialItem> RadialItems()
    {
        var actions = _radialActions.OrderBy(a => a.Order).ToList();

        // 化形爐（轉檔）還沒煉成時先佔位
        if (actions.All(a => a.Label != FurnaceLabel))
            actions.Add(new RadialAction("tianmu.island", FurnaceLabel, "轉檔", () =>
                Post(new IslandActivity("tianmu.island", "化形爐即將開爐", "轉檔功能下一版推出",
                    ActivityPriority.Normal, TimeSpan.FromSeconds(3))), 50, "ZhuShaBrush"));

        actions.Add(new RadialAction("tianmu.island", "隱藏", "天幕", () => App.Current.ToggleIsland(), 90));
        actions.Add(new RadialAction("tianmu.island", "設定", "洞府", () => App.Current.ShowSettings(), 100));

        return actions.OrderBy(a => a.Order).Take(10)
            .Select(a => new RadialItem(a.Label, a.Caption, a.Invoke, ToneFor(a.ToneKey)))
            .ToList();
    }

    /// <summary>扇區文字顏色：朱砂與天青在玄漆上提亮一些，才讀得清楚。</summary>
    private Color? ToneFor(string? key) => key switch
    {
        null => null,
        "ZhuShaBrush" => Color.FromRgb(0xE3, 0x9A, 0x8C),
        "TianQingBrush" => Color.FromRgb(0xBF, 0xD8, 0xD3),
        _ => TryFindResource(key) is SolidColorBrush b ? b.Color : null,
    };
}
