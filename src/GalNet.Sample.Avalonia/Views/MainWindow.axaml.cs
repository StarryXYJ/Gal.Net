using Avalonia.Controls;
using GalNet.Avalonia.GameView.Page;

namespace GalNet.Sample.Avalonia.Views;

public partial class MainWindow : Window
{
    public GamePage GamePage => GamePageControl;

    public MainWindow()
    {
        InitializeComponent();
    }
}
