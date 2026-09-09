using Avalonia.Input;
using Avalonia.Controls;
using GalNet.Game.Controls;
using GalNet.Avalonia.GameView.ViewModels;

namespace GalNet.Avalonia.GameView.Page;

public partial class GamePage : UserControl
{
    public DialoguePresenter Dialogue => DialogueControl;
    public Control Scene => SceneSurface;

    public GamePage() => InitializeComponent();

    private void OnAdvancePointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (DataContext is GamePageViewModel viewModel)
            viewModel.ObserveAdvancePointerPressed();
    }
}
