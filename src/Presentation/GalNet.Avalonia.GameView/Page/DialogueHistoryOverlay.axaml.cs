using GalNet.Avalonia.GameView.Presentation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace GalNet.Avalonia.GameView.Page;

public partial class DialogueHistoryOverlay : UserControl
{
    public DialogueHistoryOverlay() => InitializeComponent();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && IsVisible)
            Dispatcher.UIThread.Post(() =>
            {
                if (!IsVisible || DataContext is not GamePresentationState game) return;
                CloseHistory.Focus();
                if (game.HistoryLines.Count > 0) HistoryList.ScrollIntoView(game.HistoryLines[^1]);
            }, DispatcherPriority.Loaded);
    }
}
