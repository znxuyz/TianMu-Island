using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TianMu.Modules.Focus.Views;

public partial class FocusExpandedView : UserControl
{
    public FocusExpandedView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged old) old.PropertyChanged -= OnVmChanged;
            if (e.NewValue is INotifyPropertyChanged now) now.PropertyChanged += OnVmChanged;
            ApplyPhase();
        };
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FocusViewModel.IsBreak)) ApplyPhase();
    }

    /// <summary>專注：描金圓環、可暫歇或散功；調息：月白圓環、可出定。</summary>
    private void ApplyPhase()
    {
        bool isBreak = DataContext is FocusViewModel { IsBreak: true };
        if (TryFindResource(isBreak ? "YueBaiBrush" : "MiaoJinBrush") is Brush brush) Ring.RingBrush = brush;

        PauseButton.Visibility = isBreak ? Visibility.Collapsed : Visibility.Visible;
        AbandonButton.Visibility = isBreak ? Visibility.Collapsed : Visibility.Visible;
        SkipButton.Visibility = isBreak ? Visibility.Visible : Visibility.Collapsed;
    }
}
