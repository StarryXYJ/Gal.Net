using Avalonia.Controls;

namespace GalNet.Avalonia.GameView.Page;

/// <summary>Reusable page host. Editors and players inject different services but reuse this layout.</summary>
public partial class GameShell : UserControl
{
    public GamePage GamePage => GamePageControl;

    public GameShell() => InitializeComponent();
}
