using Avalonia.Styling;
using GalNet.Avalonia.GameView.Presentation;

namespace GalNet.Sample.CrossPlatform.Views;

public partial class GamePage : GalNet.Avalonia.GameView.Page.GamePage
{
    public GamePage(GamePresentationState state)
    {
        Presentation = state;
        InitializeComponent();
        Theme = (ControlTheme)Resources["SampleGameLayout"]!;
    }
}
