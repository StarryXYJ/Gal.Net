using Avalonia.Controls;
using GalNet.Game.Controls;
using GalNet.Avalonia.GameView.ViewModels;

namespace GalNet.Avalonia.GameView.Page;

public partial class GamePage : UserControl
{
    public DialoguePresenter Dialogue => DialogueControl;

    public GamePage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => (DataContext as GamePageViewModel)?.AttachView(this);
    }
}
