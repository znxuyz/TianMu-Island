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
        _vm = new SettingsViewModel(app.Settings, app.Updates, app.ModuleCatalog);
        DataContext = _vm;

        _vm.PropertyChanged += OnViewModelChanged;
        RefreshDynamicParts();

        SourceInitialized += (_, _) => NativeMethods.ApplyModernFrame(new WindowInteropHelper(this).Handle);
        Loaded += (_, _) => PlayInkReveal();
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

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Restart_Click(object sender, RoutedEventArgs e) => _app.Restart();
    private async void Check_Click(object sender, RoutedEventArgs e) => await _vm.CheckUpdatesAsync();
    private async void Install_Click(object sender, RoutedEventArgs e) => await _vm.InstallUpdateAsync();
    private void Skip_Click(object sender, RoutedEventArgs e) => _vm.SkipUpdate();
    private void ReleasePage_Click(object sender, RoutedEventArgs e) => _vm.OpenReleasePage();
    private void Repository_Click(object sender, RoutedEventArgs e) => _vm.OpenRepository();
    private void Folder_Click(object sender, RoutedEventArgs e) => _vm.OpenSettingsFolder();
}
