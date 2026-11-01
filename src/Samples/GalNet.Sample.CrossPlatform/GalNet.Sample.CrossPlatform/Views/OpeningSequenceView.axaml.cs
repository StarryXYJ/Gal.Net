using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Sample.CrossPlatform.Opening;

namespace GalNet.Sample.CrossPlatform.Views;

public partial class OpeningSequenceView : UserControl
{
    private readonly HashSet<Key> _heldKeys = [];
    public OpeningSequenceView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble);
    }

    public async Task PresentAsync(OpeningPageViewModel page, CancellationToken token)
    {
        // Data templates and the visual tree must exist before a page starts a media/animation operation.
        await Dispatcher.UIThread.InvokeAsync(() => { UpdateLayout(); Focus(); }, DispatcherPriority.Loaded, token);
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (DataContext is not OpeningSequence { IsRunning: true } sequence) return;
        if (args.KeyModifiers != KeyModifiers.None || args.Key != Key.Escape) return;
        args.Handled = true;
        if (!_heldKeys.Add(args.Key)) return;
        sequence.SkipCurrentCommand.Execute(null);
    }

    private void OnKeyUp(object? sender, KeyEventArgs args)
    {
        if (_heldKeys.Remove(args.Key)) args.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (DataContext is not OpeningSequence { IsRunning: true } sequence) return;
        // Buttons handle their own gesture; background clicks advance only this presentation.
        if (args.Source is Control control && (control is Button || control.GetVisualAncestors().OfType<Button>().Any())) return;
        args.Handled = true;
        if (args.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            sequence.SkipCurrentCommand.Execute(null);
    }
}
