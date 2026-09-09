using Avalonia.Controls;
using GalNet.Avalonia.GameView.Page;

namespace GalNet.Sample.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow(GameShell shell)
    {
        InitializeComponent();
        GameCanvas.GameContent = shell;
    }
}
