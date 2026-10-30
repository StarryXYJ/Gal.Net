using Ursa.Controls;

namespace GalNet.Sample.CrossPlatform.Views;

public partial class MainWindow : UrsaWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Resources.MergedDictionaries.Add(SampleCustomization.PlayerPalette.CreateResources());
        Closed += (_, _) => DetachDebugBindings();
    }

    partial void DetachDebugBindings();
}
