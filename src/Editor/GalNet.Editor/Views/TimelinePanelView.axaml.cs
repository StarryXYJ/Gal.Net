using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GalNet.Editor.Services;

namespace GalNet.Editor.Views;

public partial class TimelinePanelView : UserControl
{
    public TimelinePanelView() { InitializeComponent(); AddHandler(KeyDownEvent, OnValueKeyDown, RoutingStrategies.Tunnel); }
    private void OnValueLostFocus(object? sender, FocusChangedEventArgs args)
    { if (sender is Control { DataContext: TimelineValueRow row }) row.Commit(); }
    private void OnValueKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Source is not Control { DataContext: TimelineValueRow row }) return;
        if (args.Key == Key.Enter) { row.Commit(); args.Handled = true; }
        else if (args.Key == Key.Escape) { row.Cancel(); args.Handled = true; }
    }
}
