using System.Windows.Controls;

namespace TianMu.Modules.Music.Views;

public partial class ExpandedMusicView : UserControl
{
    public ExpandedMusicView()
    {
        InitializeComponent();
    }

    public SpectrumBars SpectrumBars => Spectrum;
}
