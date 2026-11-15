using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Data;
using System.Windows.Shell;
using TianMu.Island.Interop;

namespace TianMu.Island.Shell;

/// <summary>
/// 設定面板。開啟時內容以「墨暈」方式浮現：由模糊到清晰。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly App _app;
    private readonly SettingsViewModel _vm;

    public SettingsWindow(App app)
    {
        InitializeComponent();
        _app = app;
        _vm = new SettingsViewModel(app.Settings, app.Updates, app.ModuleCatalog, app.RadialCatalog);
        DataContext = _vm;
        SetupModuleView();

        _vm.PropertyChanged += OnViewModelChanged;
        RefreshDynamicParts();

        SourceInitialized += (_, _) => NativeMethods.ApplyModernFrame(new WindowInteropHelper(this).Handle);
        Loaded += (_, _) => PlayInkReveal();
        StateChanged += (_, _) => OnStateChanged();
        SizeChanged += (_, _) => ApplyLayout();
        ContentPanel.SizeChanged += (_, _) => ApplyLayout();
        Closed += (_, _) =>
        {
            _vm.PropertyChanged -= OnViewModelChanged;
            _vm.Detach();
        };
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e) => RefreshDynamicParts();

    /// <summary>依狀態切換按鈕與提示的顯示。</summary>
    private void RefreshDynamicParts()
    {
        ApplyScale();
        RestartBanner.Visibility = _vm.NeedsRestart ? Visibility.Visible : Visibility.Collapsed;
        InstallButton.Visibility = _vm.CanInstall ? Visibility.Visible : Visibility.Collapsed;
        AvailableLinks.Visibility = _vm.CanInstall ? Visibility.Visible : Visibility.Collapsed;
        ProgressTrack.Visibility = _vm.IsBusy && _vm.UpdateProgress > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoticeText.Visibility = string.IsNullOrEmpty(_vm.Notice) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>墨暈：內容從模糊、透明，暈開成清晰。</summary>
    private void PlayInkReveal()
    {
        var blur = new BlurEffect { Radius = 8, RenderingBias = RenderingBias.Performance };
        Root.Effect = blur;
        Root.Opacity = 0;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease };
        var sharpen = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(480)) { EasingFunction = ease };
        sharpen.Completed += (_, _) => Root.Effect = null; // 動畫結束後移除特效，文字才會銳利

        Root.BeginAnimation(OpacityProperty, fade);
        blur.BeginAnimation(BlurEffect.RadiusProperty, sharpen);
    }

    // ═══════════════════════ 分類、搜尋與版面 ═══════════════════════

    private static readonly string[] PageKeys = ["general", "look", "radial", "modules", "update", "about"];
    private FrameworkElement[] Pages => [PageGeneral, PageLook, PageRadial, PageModules, PageUpdate, PageAbout];

    /// <summary>法陣盤頁沒有逐列的設定，搜尋時用這些字比對整頁。</summary>
    private const string RadialKeywords = "法陣盤 圓盤 長按 選單 功能 扇區 格數 順序 大小 預覽 金線";

    private ICollectionView? _moduleView;
    private string _query = "";
    private (string Page, string? Section) _nav = ("general", null);

    /// <summary>法器清單：依分類分組、組內照原本順序；子分類與搜尋都用篩選。</summary>
    private void SetupModuleView()
    {
        _moduleView = CollectionViewSource.GetDefaultView(_vm.Modules);
        _moduleView.GroupDescriptions.Clear();
        _moduleView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ModuleRowViewModel.Group)));
        _moduleView.SortDescriptions.Clear();
        _moduleView.SortDescriptions.Add(new SortDescription(nameof(ModuleRowViewModel.GroupOrder), ListSortDirection.Ascending));
        _moduleView.SortDescriptions.Add(new SortDescription(nameof(ModuleRowViewModel.Index), ListSortDirection.Ascending));
        _moduleView.Filter = o => o is not ModuleRowViewModel m || (_query.Length > 0
            ? Matches(m.SearchText)
            : _nav.Page != "modules" || _nav.Section is null || m.Group == _nav.Section);
    }

    /// <summary>左側分類（Tag 是「頁」或「頁/子分類」）。</summary>
    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageGeneral is null || sender is not System.Windows.Controls.RadioButton { Tag: string tag }) return;   // 初始化途中
        var parts = tag.Split('/', 2);
        _nav = (parts[0], parts.Length > 1 ? parts[1] : null);
        if (SearchBox.Text.Length > 0) SearchBox.Text = "";   // 點分類就結束搜尋（會觸發 ApplyFilter）
        else ApplyFilter();
        ContentScroll.ScrollToTop();
    }

    private void Search_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (PageGeneral is null) return;
        _query = SearchBox.Text.Trim();
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
        ContentScroll.ScrollToTop();
    }

    private bool Matches(string text) => text.Contains(_query, StringComparison.OrdinalIgnoreCase);

    /// <summary>沒在搜尋時：只顯示選中的頁（與子分類）。搜尋時：列出所有頁裡符合的設定列。</summary>
    private void ApplyFilter()
    {
        bool searching = _query.Length > 0, any = false;
        _moduleView?.Refresh();
        for (int i = 0; i < Pages.Length; i++)
        {
            var page = Pages[i];
            string key = PageKeys[i];
            bool show = searching ? FilterPage(page, key) : key == _nav.Page;
            if (!searching) ShowSections(page, key == _nav.Page ? _nav.Section : null);
            page.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            any |= show;
        }
        SearchEmpty.Visibility = searching && !any ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>子分類：頁裡帶 Tag 的區塊只留選中的那一個（沒選子分類就全部顯示）；設定列全部還原。</summary>
    private void ShowSections(FrameworkElement page, string? section)
    {
        foreach (var row in Rows(page)) row.Visibility = Visibility.Visible;
        foreach (var sec in Sections(page))
            sec.Visibility = section is null || Equals(sec.Tag, section) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>搜尋：逐列比對文字；整頁有任何一列符合才顯示這一頁。</summary>
    private bool FilterPage(FrameworkElement page, string key)
    {
        if (key == "modules") return _vm.Modules.Any(m => Matches(m.SearchText));
        if (key == "radial") return Matches(RadialKeywords + " " + TextOf(page));

        bool any = false;
        foreach (var row in Rows(page))
        {
            bool hit = Matches(TextOf(row));
            row.Visibility = hit ? Visibility.Visible : Visibility.Collapsed;
            any |= hit;
        }
        foreach (var sec in Sections(page))
            sec.Visibility = Rows(sec).Any(r => r.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        return any;
    }

    private IEnumerable<System.Windows.Controls.Border> Rows(DependencyObject root)
    {
        var rowStyle = (Style)FindResource("Row");
        return Descendants(root).OfType<System.Windows.Controls.Border>().Where(b => ReferenceEquals(b.Style, rowStyle));
    }

    private static IEnumerable<System.Windows.Controls.StackPanel> Sections(FrameworkElement page) =>
        LogicalTreeHelper.GetChildren(page).OfType<System.Windows.Controls.StackPanel>().Where(p => p.Tag is string);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    /// <summary>一個區塊裡看得到的文字（標題、說明、按鈕與選項的字）。</summary>
    private static string TextOf(DependencyObject root) => string.Join(" ", Descendants(root).Select(d => d switch
    {
        System.Windows.Controls.TextBlock t => t.Text,
        System.Windows.Controls.ContentControl { Content: string c } => c,
        _ => null,
    }).Where(x => !string.IsNullOrEmpty(x)));

    private double _scale = 1;

    /// <summary>字體大小：整個畫面（含標頭）等比縮放；可拖曳的標頭高度跟著放大。</summary>
    private void ApplyScale()
    {
        double s = _vm.UiScale;
        if (Math.Abs(s - _scale) < 0.001 && Root.LayoutTransform is ScaleTransform) return;
        _scale = s;
        Root.LayoutTransform = new ScaleTransform(s, s);
        WindowChrome.GetWindowChrome(this).CaptionHeight = 72 * s;
        ApplyLayout();
    }

    private const double NarrowWindow = 680;   // 比這窄：左側分類變窄一點（但一直留在左邊）
    private const double WideContent = 720;    // 內容比這寬：法陣盤預覽放左邊、清單放右邊

    /// <summary>依視窗寬度調整：分類永遠在左側，窄時縮窄；法陣盤頁寬時左右並排、窄時上下排。</summary>
    private void ApplyLayout()
    {
        bool narrow = ActualWidth / _scale < NarrowWindow;
        NavColumn.Width = new GridLength(narrow ? 176 : 220);
        ContentPanel.Margin = narrow ? new Thickness(20, 0, 20, 24) : new Thickness(28, 0, 32, 32);

        bool wide = ContentPanel.ActualWidth >= WideContent;
        System.Windows.Controls.Grid.SetColumnSpan(RadialPreviewCard, wide ? 1 : 2);
        System.Windows.Controls.Grid.SetRow(RadialList, wide ? 0 : 1);
        System.Windows.Controls.Grid.SetColumn(RadialList, wide ? 1 : 0);
        System.Windows.Controls.Grid.SetColumnSpan(RadialList, wide ? 1 : 2);
        RadialList.Margin = wide ? new Thickness(28, 0, 0, 0) : new Thickness(0, 16, 0, 0);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>放大時：無框視窗會超出螢幕邊緣一點，內縮回來；按鈕換成「還原」。</summary>
    private void OnStateChanged()
    {
        bool max = WindowState == WindowState.Maximized;
        Root.Margin = max ? new Thickness(7) : new Thickness(0);
        MaximizeButton.Content = max ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = max ? "還原" : "放大";
        System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, max ? "還原" : "放大");
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Restart_Click(object sender, RoutedEventArgs e) => _app.Restart();
    private async void Check_Click(object sender, RoutedEventArgs e) => await _vm.CheckUpdatesAsync();
    private async void Install_Click(object sender, RoutedEventArgs e) => await _vm.InstallUpdateAsync();
    private void Skip_Click(object sender, RoutedEventArgs e) => _vm.SkipUpdate();
    private void ReleasePage_Click(object sender, RoutedEventArgs e) => _vm.OpenReleasePage();
    private void Repository_Click(object sender, RoutedEventArgs e) => _vm.OpenRepository();
    private void Folder_Click(object sender, RoutedEventArgs e) => _vm.OpenSettingsFolder();
}
