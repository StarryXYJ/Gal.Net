using Avalonia.Controls;
using GalNet.Avalonia.GameView.Page;
using Ursa.Controls;

namespace GalNet.Sample.Avalonia.Views;

public partial class MainWindow : UrsaWindow
{
    public MainWindow(GameShell shell)
    {
        InitializeComponent();
        GameCanvas.GameContent = shell;
    }
}
