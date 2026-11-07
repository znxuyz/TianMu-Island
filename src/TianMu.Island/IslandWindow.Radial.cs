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
    private IFileFurnace? _furnace;
    private DispatcherTimer? _longPress;
    private RadialMenuWindow? _radial;
    private bool _radialFromPress;

    public void AddRadialAction(RadialAction action) => Dispatcher.InvokeAsync(() =>
    {
        _radialActions.RemoveAll(a => a.ModuleId == action.ModuleId && a.Label == action.Label);
        _radialActions.Add(action);
    });

    public void SetFurnace(IFileFurnace furnace) => Dispatcher.InvokeAsync(() => _furnace = furnace);

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
        CancelDragHover();
        NativeMethods.GetCursorPos(out var cursor);
        var screen = Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y));
        var dpi = VisualTreeHelper.GetDpi(this);
        var center = new Point(cursor.X / dpi.DpiScaleX, cursor.Y / dpi.DpiScaleY);

        var context = new RadialContext(_furnace,
            (title, subtitle) => Post(new IslandActivity("tianmu.convert", title, subtitle, ActivityPriority.Normal, TimeSpan.FromSeconds(4))
            {
                GlowKey = title.Contains("失敗", StringComparison.Ordinal) ? "ZhuShaBrush" : "TianQingBrush",
            }),
            PickFiles);
        _radial = new RadialMenuWindow(RadialItems(), context);
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

        // 化形爐：有轉檔引擎時收檔案（點它則先選檔案）；沒有時（法器被關閉）提示一下
        if (actions.All(a => a.Label != FurnaceLabel))
            actions.Add(new RadialAction("tianmu.island", FurnaceLabel, "轉檔", () =>
                Post(new IslandActivity("tianmu.island", "化形爐已關閉", "到設定 → 法器打開「化形爐」",
                    ActivityPriority.Normal, TimeSpan.FromSeconds(3))), 50, "ZhuShaBrush"));

        actions.Add(new RadialAction("tianmu.island", "隱藏", "天幕", () => App.Current.ToggleIsland(), 90));
        actions.Add(new RadialAction("tianmu.island", "設定", "洞府", () => App.Current.ShowSettings(), 100));

        return actions.OrderBy(a => a.Order).Take(10)
            .Select(a => new RadialItem(a.Label, a.Caption, a.Invoke, ToneFor(a.ToneKey), a.OnFiles,
                IsFurnace: a.Label == FurnaceLabel && _furnace is not null))
            .ToList();
    }

    /// <summary>沒拖檔案就點化形爐：開檔案選擇視窗。</summary>
    private static IReadOnlyList<string>? PickFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "放進化形爐的檔案",
            Multiselect = true,
            CheckFileExists = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileNames : null;
    }

    // ═══════════════════════ 拖著檔案停在天幕上 ═══════════════════════

    private DispatcherTimer? _dragHover;

    /// <summary>檔案拖進天幕時開始計時；停半秒就展開法陣盤（可以放進化形爐或乾坤袋）。</summary>
    private void StartDragHover()
    {
        if (_radial is not null) return;
        _dragHover ??= new DispatcherTimer(LongPressDelay, DispatcherPriority.Input, (_, _) =>
        {
            _dragHover!.Stop();
            if (!_dropping || _radial is not null) return;
            EndDragHint();       // 收起乾坤袋的漩渦提示，改由法陣盤接手
            OpenRadial();
        }, Dispatcher);
        if (!_dragHover.IsEnabled) _dragHover.Start();
    }

    private void CancelDragHover() => _dragHover?.Stop();

    /// <summary>扇區文字顏色：朱砂與天青在玄漆上提亮一些，才讀得清楚。</summary>
    private Color? ToneFor(string? key) => key switch
    {
        null => null,
        "ZhuShaBrush" => Color.FromRgb(0xE3, 0x9A, 0x8C),
        "TianQingBrush" => Color.FromRgb(0xBF, 0xD8, 0xD3),
        _ => TryFindResource(key) is SolidColorBrush b ? b.Color : null,
    };
}
