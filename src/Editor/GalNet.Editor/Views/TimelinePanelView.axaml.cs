using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.Services;

namespace GalNet.Editor.Views;

public partial class TimelinePanelView : UserControl
{
    private Flyout? _objectFlyout;
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
    private void OnAddObject(object? sender, RoutedEventArgs args) { if (sender is Button button) OpenObjects(button, null); }
    private void OnReplaceObject(object? sender, RoutedEventArgs args) { if (sender is Button { DataContext: TimelineTreeNode node } button) OpenObjects(button, node.ObjectHandle); }
    private async void OpenObjects(Button button, string? handle)
    {
        _objectFlyout?.Hide();
        if (DataContext is not ViewModels.TimelinePanelViewModel vm || vm.Session.BeginObjectEdit(handle) is not { } draft) return;
        try
        {
            await draft.PrepareAsync(); if (!draft.IsCurrent) return;
            var picker = new SceneTargetPicker { Width = 500, Height = 420, Margin = new(10) };
            picker.Configure(draft.Selection);
            var flyout = new Flyout { Content = picker }; _objectFlyout = flyout;
            Avalonia.Controls.Primitives.FlyoutBase.SetAttachedFlyout(button, flyout);
            void StateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) { if (!draft.IsCurrent) flyout.Hide(); }
            vm.Session.PropertyChanged += StateChanged;
            picker.Cancelled += () => flyout.Hide();
            picker.Confirmed += () => { if (draft.Commit()) flyout.Hide(); else picker.ResourcePanel.Hint = draft.Message; };
            flyout.Closed += (_, _) =>
            {
                vm.Session.PropertyChanged -= StateChanged; picker.Dispose(); flyout.Content = null;
                if (ReferenceEquals(vm.Session.ObjectEdit, draft)) vm.Session.EndObjectEdit();
                if (ReferenceEquals(_objectFlyout, flyout)) _objectFlyout = null;
                if (ReferenceEquals(Avalonia.Controls.Primitives.FlyoutBase.GetAttachedFlyout(button), flyout)) Avalonia.Controls.Primitives.FlyoutBase.SetAttachedFlyout(button, null);
            };
            flyout.ShowAt(button); Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (flyout.IsOpen) picker.ResourcePanel.Search.Focus(); });
        }
        catch (OperationCanceledException) { }
        catch (Exception) { vm.Session.Message = vm.Session.SceneText["Scene.LoadFailed"]; vm.Session.EndObjectEdit(); }
    }
    private void OnAddProperty(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: TimelineTreeNode { ObjectHandle: { } handle } } button
            || DataContext is not ViewModels.TimelinePanelViewModel vm) return;
        var menu = new MenuFlyout();
        foreach (var row in vm.Session.AvailableProperties(handle))
        {
            var option = new MenuItem { Header = row.Label, DataContext = row, Name = "AddTimelinePropertyOption" };
            option.Click += (_, _) => vm.Session.AddProperty(row);
            menu.Items.Add(option);
        }
        if (menu.Items.Count == 0) return;
        Avalonia.Controls.Primitives.FlyoutBase.SetAttachedFlyout(button, menu);
        menu.Closed += (_, _) => Avalonia.Controls.Primitives.FlyoutBase.SetAttachedFlyout(button, null);
        menu.ShowAt(button); args.Handled = true;
    }
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e) { _objectFlyout?.Hide(); base.OnDetachedFromVisualTree(e); }
}
