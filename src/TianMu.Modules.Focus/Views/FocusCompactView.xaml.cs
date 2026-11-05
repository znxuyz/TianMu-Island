using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TianMu.Modules.Focus.Views;

public partial class FocusCompactView : UserControl
{
    public FocusCompactView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged old) old.PropertyChanged -= OnVmChanged;
            if (e.NewValue is INotifyPropertyChanged now) now.PropertyChanged += OnVmChanged;
            ApplyPhaseColor();
        };
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FocusViewModel.IsBreak)) ApplyPhaseColor();
    }

    /// <summary>專注是描金，調息是月白。</summary>
    private void ApplyPhaseColor()
    {
        bool isBreak = DataContext is FocusViewModel { IsBreak: true };
        string key = isBreak ? "YueBaiBrush" : "MiaoJinBrush";
        if (TryFindResource(key) is Brush brush)
        {
            Ring.RingBrush = brush;
            Label.Foreground = brush;
        }
    }
}
