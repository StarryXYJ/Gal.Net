using Avalonia.Controls;
using GalNet.Control.Abstraction.UI;

namespace GalNet.Control.Screen.BuiltIn;

/// <summary>
/// Game screen root layout — holds LayerCanvas, DialogueHost, ChoiceHost,
/// ScreenOverlay, and ClickIndicator. Used internally by DefaultGameView.
/// </summary>
public partial class GameScreenView : UserControl
{
    public GameScreenView()
    {
        InitializeComponent();
        Bind(BackgroundProperty, PaletteBinding.Create(this, "Background0"));
        ClickIndicator.Bind(TextBlock.ForegroundProperty, PaletteBinding.Create(ClickIndicator, "PrimaryColor"));
    }
}
