using Avalonia.Controls;

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
    }
}
