using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Controls;
using GalNet.Game.Controls;
using GalNet.Avalonia.GameView.ViewModels;

namespace GalNet.Avalonia.GameView.Page;

public partial class GamePage : UserControl
{
    public DialoguePresenter Dialogue => DialogueControl;
    public Control Scene => SceneSurface;

    public GamePage()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPagePointerPressed, RoutingStrategies.Tunnel);
    }

    private void OnPagePointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (DataContext is not GamePageViewModel viewModel || IsPlayerControl(eventArgs.Source))
            return;

        viewModel.ObserveAdvancePointerPressed();
        if (viewModel.AdvanceCommand.CanExecute(null))
            viewModel.AdvanceCommand.Execute(null);
        eventArgs.Handled = true;
    }

    private static bool IsPlayerControl(object? source)
    {
        for (var current = source as Control; current is not null; current = current.GetVisualParent() as Control)
        {
            if (current is Button or ChoiceList)
                return true;
        }

        return false;
    }
}
