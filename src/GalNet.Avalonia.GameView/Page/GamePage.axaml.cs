using Avalonia;
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
        // Input from a ListBoxItem can originate at a non-Control visual such as its
        // text presenter. Walk every visual ancestor so choice input is never confused
        // with scene input by this global advance handler.
        for (var current = source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Button or ChoiceList)
                return true;
        }

        return false;
    }
}
