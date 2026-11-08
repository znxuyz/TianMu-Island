using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TianMu.Modules.Cleanse.Views;

public partial class LowSpaceView : UserControl
{
    public LowSpaceView() => InitializeComponent();
}

/// <summary>低空間提醒的資料：剩餘比例畫成朱砂環。</summary>
public sealed class LowSpaceViewModel
{
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required double FreeFraction { get; init; }
    public required ICommand OpenCommand { get; init; }
    public required ICommand LaterCommand { get; init; }

    /// <summary>圓環的虛線長度：直徑 42、線寬 4，圓周約 33 個線寬單位。</summary>
    public DoubleCollection Dash
    {
        get
        {
            double circumference = Math.PI * 42 / 4;
            double on = Math.Max(0.5, circumference * Math.Clamp(FreeFraction, 0, 1));
            return [on, 100];
        }
    }
}
