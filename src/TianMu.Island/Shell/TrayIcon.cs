using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using TianMu.Island.Interop;
using TianMu.Island.Update;
using Forms = System.Windows.Forms;

namespace TianMu.Island.Shell;

/// <summary>
/// 系統匣圖示：左鍵開啟設定（天幕隱藏時則是叫回天幕）、右鍵叫出選單。
/// 圖示本體用 WinForms 的 NotifyIcon（最穩定，工作列重啟也會自動重建），
/// 選單則用 WPF 自己畫，才能套用「玄漆描金」的樣式。
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly App _app;
    private readonly Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon? _iconImage;
    private ContextMenu? _menu;

    public TrayIcon(App app)
    {
        _app = app;
        _iconImage = LoadIcon();
        _icon = new Forms.NotifyIcon
        {
            Icon = _iconImage ?? System.Drawing.SystemIcons.Application,
            Text = TooltipText(),
            Visible = true,
        };
        _icon.MouseUp += OnMouseUp;
        _app.Updates.Changed += () => _icon.Text = TooltipText();
    }

    private string TooltipText() =>
        _app.Updates.Stage == UpdateStage.Available
            ? $"天幕島 {UpdateCoordinator.CurrentVersionText} · 有新版本"
            : $"天幕島 {UpdateCoordinator.CurrentVersionText}";

    private void OnMouseUp(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
        {
            if (_app.IsIslandVisible) _app.ShowSettings();
            else _app.ToggleIsland();
        }
        else if (e.Button == Forms.MouseButtons.Right) ShowMenu();
    }

    private void ShowMenu()
    {
        if (_menu is { IsOpen: true }) _menu.IsOpen = false;

        var menu = new ContextMenu
        {
            Style = (Style)_app.FindResource("TianMu.ContextMenu"),
            Placement = PlacementMode.MousePoint,
        };

        // 法器捷徑（凝神、飲水…）
        var quick = _app.QuickActions;
        foreach (var action in quick)
            menu.Items.Add(Item(action.Label, action.Invoke, accent: true));
        if (quick.Count > 0)
            menu.Items.Add(new Separator { Style = (Style)_app.FindResource("TianMu.Separator") });

        menu.Items.Add(Item("開啟設定", _app.ShowSettings));
        menu.Items.Add(Item(_app.IsIslandVisible ? "暫時隱藏天幕" : "顯示天幕", _app.ToggleIsland));

        if (_app.Updates.Stage == UpdateStage.Available && _app.Updates.Available is { } release)
            menu.Items.Add(Item($"更新至 {release.Tag}", () => _ = _app.Updates.InstallAsync(), accent: true));

        menu.Items.Add(new Separator { Style = (Style)_app.FindResource("TianMu.Separator") });
        menu.Items.Add(Item("結束天幕島", _app.Quit));

        // 讓選單所在的視窗成為前景，點選單外面時選單才會正常關閉
        menu.Opened += (_, _) =>
        {
            if (PresentationSource.FromVisual(menu) is HwndSource source)
                NativeMethods.SetForegroundWindow(source.Handle);
        };

        _menu = menu;
        menu.IsOpen = true;
    }

    /// <summary>在系統匣跳出一則提示（例如告訴使用者天幕藏到哪了）。</summary>
    public void ShowHint(string title, string text)
    {
        try { _icon.ShowBalloonTip(5000, title, text, Forms.ToolTipIcon.None); }
        catch { /* 通知被關閉就算了 */ }
    }

    private MenuItem Item(string header, Action onClick, bool accent = false)
    {
        var item = new MenuItem
        {
            Header = header,
            Style = (Style)_app.FindResource(accent ? "TianMu.MenuItem.Accent" : "TianMu.MenuItem"),
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    private static System.Drawing.Icon? LoadIcon()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/TianMuIsland;component/Assets/TianMu.ico"));
            if (info is null) return null;
            using var stream = info.Stream;
            return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _iconImage?.Dispose();
    }
}
