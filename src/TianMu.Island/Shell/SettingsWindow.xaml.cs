using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
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

    // ═══════════════════════ 分類與版面 ═══════════════════════

    /// <summary>左邊（或上方）的分類：只顯示選中的那一類設定。</summary>
    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageGeneral is null) return;   // InitializeComponent 途中預設選中「一般」時，頁面還沒建立
        (System.Windows.Controls.RadioButton Nav, FrameworkElement Page)[] map =
        [
            (NavGeneral, PageGeneral), (NavLook, PageLook), (NavRadial, PageRadial),
            (NavModules, PageModules), (NavUpdate, PageUpdate), (NavAbout, PageAbout),
        ];
        foreach (var (nav, page) in map)
            page.Visibility = nav.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ContentScroll.ScrollToTop();
    }

    private const double NarrowWindow = 720;   // 比這窄：分類改排在上方
    private const double WideContent = 720;    // 內容比這寬：法陣盤預覽放左邊、清單放右邊

    /// <summary>依視窗寬度重排：寬時分類在左、法陣盤左右並排；窄時分類在上、法陣盤上下排。</summary>
    private void ApplyLayout()
    {
        bool narrow = ActualWidth < NarrowWindow;
        NavColumn.Width = narrow ? new GridLength(0) : new GridLength(200);
        NavPanel.Orientation = narrow ? System.Windows.Controls.Orientation.Horizontal : System.Windows.Controls.Orientation.Vertical;
        NavPanel.Margin = narrow ? new Thickness(24, 4, 24, 8) : new Thickness(20, 12, 8, 20);
        System.Windows.Controls.Grid.SetRow(NavPanel, 0);
        System.Windows.Controls.Grid.SetRowSpan(NavPanel, narrow ? 1 : 2);
        System.Windows.Controls.Grid.SetColumnSpan(NavPanel, narrow ? 2 : 1);
        System.Windows.Controls.Grid.SetRow(ContentScroll, narrow ? 1 : 0);
        System.Windows.Controls.Grid.SetRowSpan(ContentScroll, narrow ? 1 : 2);
        System.Windows.Controls.Grid.SetColumn(ContentScroll, narrow ? 0 : 1);
        System.Windows.Controls.Grid.SetColumnSpan(ContentScroll, narrow ? 2 : 1);
        ContentPanel.Margin = narrow ? new Thickness(24, 0, 24, 24) : new Thickness(16, 0, 32, 32);

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
