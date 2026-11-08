using System.ComponentModel;
using System.Windows;

namespace TianMu.Modules.Cleanse.Views;

/// <summary>洗髓的審查視窗。</summary>
public partial class CleanseWindow : Window
{
    private readonly CleanseViewModel _vm;

    public CleanseWindow(CleanseViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.PropertyChanged += OnChanged;
        SizeChanged += (_, _) => UpdateBar();
        Loaded += (_, _) => UpdateBar();
        Closed += (_, _) =>
        {
            vm.PropertyChanged -= OnChanged;
            vm.Cancel();
        };
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CleanseViewModel.UsedFraction)) UpdateBar();
    }

    /// <summary>用量條的長度（依目前寬度算）。</summary>
    private void UpdateBar()
    {
        if (UsedBar.Parent is FrameworkElement track && track.ActualWidth > 0)
            UsedBar.Width = Math.Clamp(_vm.UsedFraction, 0, 1) * track.ActualWidth;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
