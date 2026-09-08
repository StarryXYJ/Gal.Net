using Avalonia.Controls;
using GalNet.Avalonia.GameView.Page;

namespace GalNet.Sample.Avalonia.Views;

public partial class MainWindow : Window
{
    public GameShell GameShell => GameShellControl;

    public MainWindow()
    {
        InitializeComponent();
    }
}
