using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace TianMu.Modules.Wenfang.Shared;

/// <summary>
/// 墨書、算籌、畫卷共用的視窗外框（仿 Office 的版面）：
///   標題列（朱砂印、檔名、快速存取：儲存／復原／取消復原）→ 功能區分頁 → 功能區 → 編輯區 → 狀態列
/// 檔案的開啟、儲存、另存、未存檔提醒都在這裡；各編輯器只負責「讀」「寫」與編輯區本身。
/// </summary>
internal abstract class EditorWindow : Window
{
    private readonly TextBlock _title = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly ContentControl _ribbon = new();
    private readonly ContentControl _body = new();
    private readonly string _appName;
    private bool _dirty;

    protected readonly TextBlock Status = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Secondary };
    protected readonly StackPanel StatusRight = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

    protected EditorWindow(string seal, string appName)
    {
        _appName = appName;
        Resources.MergedDictionaries.Add(Ui.Styles);
        Width = 1200; Height = 820; MinWidth = 720; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        Background = Ui.Lacquer;
        Foreground = Ui.Moon;
        FontFamily = Ui.Sans;
        UseLayoutRounding = true;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 44, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0, 0, 0, 1), UseAeroCaptionButtons = false });

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });

        // ── 標題列 ──
        var bar = new DockPanel { Margin = new Thickness(12, 0, 4, 0) };
        var sealBox = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(4), Background = Ui.Cinnabar, Margin = new Thickness(0, 0, 10, 0),
            Child = new TextBlock { Text = seal, FontFamily = Ui.Serif, FontWeight = FontWeights.Bold, FontSize = 14, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        DockPanel.SetDock(sealBox, Dock.Left);
        bar.Children.Add(sealBox);
        var quick = Ui.Row(
            Chromed(Ui.Small("", "儲存（Ctrl+S）", () => Save())),
            Chromed(Ui.Small("", "復原（Ctrl+Z）", Undo)),
            Chromed(Ui.Small("", "取消復原（Ctrl+Y）", Redo)));
        quick.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(quick, Dock.Left);
        bar.Children.Add(quick);
        var caption = Ui.Row(
            Chromed(Ui.Small("", "最小化", () => WindowState = WindowState.Minimized)),
            Chromed(Ui.Small("", "放大／還原", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized)),
            Chromed(Ui.Small("", "關閉", Close)));
        DockPanel.SetDock(caption, Dock.Right);
        bar.Children.Add(caption);
        bar.Children.Add(_title);
        root.Children.Add(bar);

        // ── 功能區分頁與功能區 ──
        var tabRow = new Border { Child = _tabs, Padding = new Thickness(8, 0, 8, 0), BorderBrush = Ui.Hairline, BorderThickness = new Thickness(0, 0, 0, 1) };
        Grid.SetRow(tabRow, 1);
        root.Children.Add(tabRow);
        var ribbonHost = new Border { Child = _ribbon, MinHeight = 86, Padding = new Thickness(6, 2, 6, 2), Background = Ui.Hover, BorderBrush = Ui.Hairline, BorderThickness = new Thickness(0, 0, 0, 1) };
        Grid.SetRow(ribbonHost, 2);
        root.Children.Add(ribbonHost);

        Grid.SetRow(_body, 3);
        root.Children.Add(_body);

        var status = new DockPanel { Margin = new Thickness(14, 0, 14, 0) };
        DockPanel.SetDock(StatusRight, Dock.Right);
        status.Children.Add(StatusRight);
        status.Children.Add(Status);
        Grid.SetRow(status, 4);
        root.Children.Add(status);

        Content = root;
        StateChanged += (_, _) => root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        AllowDrop = true;
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) foreach (var f in files) WenfangHub.Open(f);
        };
        RefreshTitle();
    }

    private static Button Chromed(Button b)
    {
        WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        return b;
    }

    // ═══════════════════════ 功能區 ═══════════════════════

    private readonly List<(RadioButton Tab, FrameworkElement Panel)> _pages = [];

    /// <summary>加一個功能區分頁（「常用」「插入」……），第一個加入的預設選中。</summary>
    protected void AddTab(string name, params FrameworkElement[] groups)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var g in groups) panel.Children.Add(g);
        var tab = new RadioButton { Content = name, GroupName = "WfRibbon" + GetHashCode(), Style = (Style)Ui.Styles["WfTab"] };
        tab.Checked += (_, _) => _ribbon.Content = panel;
        _tabs.Children.Add(tab);
        _pages.Add((tab, panel));
        if (_pages.Count == 1) tab.IsChecked = true;
    }

    /// <summary>每個編輯器都有的「檔案」分頁。</summary>
    protected FrameworkElement FileGroup() => Ui.Group("檔案",
        Ui.Row(
            Ui.Big("", "新增", NewDocument, "開一份新的（Ctrl+N）"),
            Ui.Big("", "開啟", OpenDialog, "開啟檔案（Ctrl+O）"),
            Ui.Big("", "儲存", () => Save(), "儲存（Ctrl+S）"),
            Ui.Big("", "另存新檔", () => Save(saveAs: true), "另存新檔（F12）"),
            Ui.Big("", "列印", Print, "列印；印表機選「Microsoft Print to PDF」就能存成 PDF（Ctrl+P）")));

    protected void SetBody(UIElement body) => _body.Content = body;

    // ═══════════════════════ 檔案 ═══════════════════════

    public string? FilePath { get; protected set; }

    /// <summary>開啟對話框的篩選字串，例如「文件|*.docx;*.rtf;*.txt」。</summary>
    protected abstract string OpenFilter { get; }

    /// <summary>儲存時的篩選字串；第一個是預設格式。</summary>
    protected abstract string SaveFilter { get; }

    protected abstract string DefaultExtension { get; }

    /// <summary>讀檔到編輯區（失敗時丟出例外，外框會告訴使用者）。</summary>
    public abstract void Load(string path);

    /// <summary>把編輯區寫到檔案。</summary>
    protected abstract void Write(string path);

    protected abstract void NewDocument();
    protected abstract void Undo();
    protected abstract void Redo();
    protected abstract void Print();

    protected bool Dirty
    {
        get => _dirty;
        set { if (_dirty == value) return; _dirty = value; RefreshTitle(); }
    }

    protected void RefreshTitle()
    {
        string name = FilePath is null ? $"未命名{_appName}" : Path.GetFileName(FilePath);
        _title.Text = $"{(Dirty ? "● " : "")}{name} — {_appName}";
        Title = _title.Text;
    }

    private void OpenDialog()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = OpenFilter, Title = $"開啟 — {_appName}" };
        if (dlg.ShowDialog(this) == true) WenfangHub.Open(dlg.FileName);
    }

    /// <summary>開啟檔案到這個視窗（由 WenfangHub 呼叫）。</summary>
    public bool TryLoad(string path)
    {
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            Load(path);
            FilePath = path;
            Dirty = false;
            WenfangHub.Recent.Add(path);
            RefreshTitle();
            return true;
        }
        catch (Exception e)
        {
            MessageBox.Show(this, $"打不開這個檔案：\n{path}\n\n{e.Message}", _appName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally { Mouse.OverrideCursor = null; }
    }

    protected bool Save(bool saveAs = false)
    {
        string? path = FilePath;
        bool native = path is not null && path.EndsWith(DefaultExtension, StringComparison.OrdinalIgnoreCase);
        if (saveAs || path is null || (!native && !IsWritableFormat(path)))
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = SaveFilter, DefaultExt = DefaultExtension, AddExtension = true, Title = $"另存新檔 — {_appName}",
                FileName = path is null ? $"未命名{_appName}" : Path.GetFileNameWithoutExtension(path),
                InitialDirectory = path is null ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : Path.GetDirectoryName(path),
            };
            if (dlg.ShowDialog(this) != true) return false;
            path = dlg.FileName;
        }
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            Write(path);
            FilePath = path;
            Dirty = false;
            WenfangHub.Recent.Add(path);
            Status.Text = $"已儲存 {DateTime.Now:HH:mm}";
            return true;
        }
        catch (Exception e)
        {
            MessageBox.Show(this, $"存不起來：\n{path}\n\n{e.Message}\n\n檔案可能正被其他程式開著。", _appName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally { Mouse.OverrideCursor = null; }
    }

    /// <summary>這個副檔名能直接存回去（例如 .rtf、.csv）；不能的就改問另存新檔。</summary>
    protected virtual bool IsWritableFormat(string path) => false;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.S) { Save(); e.Handled = true; }
        else if (ctrl && e.Key == Key.O) { OpenDialog(); e.Handled = true; }
        else if (ctrl && e.Key == Key.N) { NewDocument(); e.Handled = true; }
        else if (ctrl && e.Key == Key.P) { Print(); e.Handled = true; }
        else if (e.Key == Key.F12) { Save(saveAs: true); e.Handled = true; }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (Dirty)
        {
            var r = MessageBox.Show(this, $"「{(FilePath is null ? "未命名" : Path.GetFileName(FilePath))}」還沒儲存，要先存起來嗎？", _appName,
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel || (r == MessageBoxResult.Yes && !Save())) { e.Cancel = true; return; }
        }
        base.OnClosing(e);
    }
}
