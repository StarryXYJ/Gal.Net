using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using GalNet.Avalonia.GameView.ViewModels;

namespace GalNet.Avalonia.GameView.Page;

/// <summary>Player-root modal surface; its appearance belongs to the selected theme.</summary>
public sealed class SkipConfirmation : TemplatedControl
{
    public SkipConfirmation() => Focusable = true;
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && IsVisible) Focus();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape || DataContext is not GamePageViewModel state) return;
        state.CancelSkipCommand.Execute(null);
        e.Handled = true;
    }
}
