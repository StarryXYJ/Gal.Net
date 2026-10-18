using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GalNet.Editor.Services;

namespace GalNet.Editor.Views;

public partial class TimelinePanelView : UserControl
{
    public TimelinePanelView() { InitializeComponent(); AddHandler(KeyDownEvent, OnValueKeyDown, RoutingStrategies.Tunnel); }
    private void OnValueLostFocus(object? sender, FocusChangedEventArgs args)
    { if (sender is Control { DataContext: TimelineValueRow row }) row.Commit(); }
    private void OnValueKeyDown(object? sender, KeyEventArgs args)
    {
        var timing = args.Source as NumericUpDown ?? (args.Source as Control)?.FindAncestorOfType<NumericUpDown>();
        if (timing?.Name is "TimelineDurationDraft" or "TimelineFpsDraft" && DataContext is ViewModels.TimelinePanelViewModel vm)
        {
            if (args.Key == Key.Enter) { vm.Session.CommitTimingInput(timing.Name == "TimelineFpsDraft"); args.Handled = true; }
            else if (args.Key == Key.Escape) { vm.Session.CancelTimingInput(timing.Name == "TimelineFpsDraft"); args.Handled = true; }
            return;
        }
        if (args.Source is not Control { DataContext: TimelineValueRow row }) return;
        if (args.Key == Key.Enter) { row.Commit(true); args.Handled = true; }
        else if (args.Key == Key.Escape) { row.Cancel(); args.Handled = true; }
    }
    private void OnTimingLostFocus(object? sender, FocusChangedEventArgs args)
    { if (sender is NumericUpDown input && DataContext is ViewModels.TimelinePanelViewModel vm) vm.Session.CommitTimingInput(input.Name == "TimelineFpsDraft"); }
    private void OnBlendModeSelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (sender is not ComboBox { DataContext: TimelineValueRow row } input || input.SelectedIndex is not (0 or 1)) return;
        row.BlendModeIndex = input.SelectedIndex;
        input.SetCurrentValue(Avalonia.Controls.Primitives.SelectingItemsControl.SelectedIndexProperty, row.BlendModeIndex);
        args.Handled = true;
    }
    private void OnTrackClick(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (sender is CheckBox { DataContext: TimelineValueRow row } check)
        {
            if (!row.ToggleTrack()) check.SetCurrentValue(Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty, row.HasTrack);
            args.Handled = true;
        }
    }
    private void OnTreeRowPointerPressed(object? sender, PointerPressedEventArgs args)
    { if (sender is Control { DataContext: TimelineTreeNode { Value: { } row } } && DataContext is ViewModels.TimelinePanelViewModel vm) vm.Session.SetActiveTrack(row); }
}
