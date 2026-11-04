using System.Windows.Controls;

namespace TianMu.Modules.Music.Views;

public partial class CompactMusicView : UserControl
{
    public CompactMusicView()
    {
        InitializeComponent();
    }

    public SpectrumBars SpectrumBars => Spectrum;
}
