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
    private Flyout? _keyFlyout;
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
    private void OnTreeContextRequested(object? sender, ContextRequestedEventArgs args)
    {
        if (sender is not Control { DataContext: TimelineTreeNode node } control
            || DataContext is not ViewModels.TimelinePanelViewModel vm || !vm.Session.VisibleNodes.Contains(node)) return;
        var menu = new MenuFlyout();
        var remove = new MenuItem { Name = "RemoveTimelineOverride", Header = vm.Session.SceneText["Timeline.RemoveOverride"] };
        remove.Click += (_, _) => vm.Session.RemoveOverride(node);
        menu.Items.Add(remove);
        Avalonia.Controls.Primitives.FlyoutBase.SetAttachedFlyout(control, menu);
        menu.Closed += (_, _) => Avalonia.Controls.Primitives.FlyoutBase.SetAttachedFlyout(control, null);
        menu.ShowAt(control); args.Handled = true;
    }
    private void OnTreeRowPointerPressed(object? sender, PointerPressedEventArgs args)
    { if (sender is Control { DataContext: TimelineTreeNode { Value: { } row } } && DataContext is ViewModels.TimelinePanelViewModel vm) vm.Session.SetActiveTrack(row); }
    private void OnAddObject(object? sender, RoutedEventArgs args) { if (sender is Button button) OpenObjects(button); }
    private async void OpenObjects(Button button)
    {
        _objectFlyout?.Hide();
        if (DataContext is not ViewModels.TimelinePanelViewModel vm || vm.Session.BeginObjectEdit() is not { } draft) return;
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
    private void OnFitCurve(object? sender, RoutedEventArgs args)
    { if (sender is Button { DataContext: TimelineTreeNode node } && DataContext is ViewModels.TimelinePanelViewModel vm) vm.Session.FitCurve(node); }
    private void OnKeyContextRequested(object? sender, GalNet.Game.Controls.Timeline.TimelineKeyContextEventArgs args)
    {
        if (sender is not Control anchor || DataContext is not ViewModels.TimelinePanelViewModel vm || vm.Session.KeyEdit is not { } draft) return;
        var text = vm.Session.SceneText; var menu = new MenuFlyout();
        MenuItem Item(string name, string label, Action action, bool selected = false)
        {
            var item = new MenuItem { Name = name, Header = text[label], ToggleType = MenuItemToggleType.CheckBox, IsChecked = selected };
            item.Click += (_, _) => { action(); menu.Hide(); }; return item;
        }
        var interpolation = new MenuItem { Header = text["Timeline.Curve.Interpolation"] };
        foreach (var mode in Enum.GetValues<GalNet.Core.Scene.AnimationInterpolation>())
        {
            var captured = mode;
            interpolation.Items.Add(Item("TimelineInterpolation" + mode, mode == GalNet.Core.Scene.AnimationInterpolation.CubicHermite ? "Timeline.Curve.Hermite" : "Timeline.Key." + mode,
                () => { draft.InterpolationIndex = (int)captured; draft.CommitInterpolation(); }, draft.InterpolationIndex == (int)mode));
        }
        menu.Items.Add(interpolation);
        foreach (bool? side in new bool?[] { null, true, false })
        {
            var group = new MenuItem { Header = text[side is null ? "Timeline.Curve.Both" : side.Value ? "Timeline.Key.Incoming" : "Timeline.Key.Outgoing"] };
            foreach (var mode in Enum.GetValues<TimelineTangentMode>())
            {
                var captured = mode; var selected = draft.TangentModes.All(settings => (side == false || settings.Incoming == mode) && (side == true || settings.Outgoing == mode));
                group.Items.Add(Item("TimelineMode" + (side is null ? "Both" : side.Value ? "Incoming" : "Outgoing") + mode,
                    "Timeline.Curve." + mode, () => draft.SetMode(captured, side), selected));
            }
            menu.Items.Add(group);
        }
        menu.Items.Add(Item("TimelineLinkTangents", "Timeline.Curve.Linked", () => draft.SetLinked(true), draft.TangentModes.All(settings => settings.Linked)));
        menu.Items.Add(Item("TimelineBreakTangents", "Timeline.Curve.Broken", () => draft.SetLinked(false), draft.TangentModes.All(settings => !settings.Linked)));
        var precise = new MenuItem { Name = "TimelineEditKeyValues", Header = text["Timeline.Curve.EditValues"] };
        precise.Click += (_, _) =>
        {
            menu.Hide(); if (!draft.IsCurrent) return;
            _keyFlyout?.Hide(); var editor = new TimelineKeyEditorView(); editor.Configure(vm.Session);
            var flyout = new Flyout { Content = editor }; _keyFlyout = flyout;
            Avalonia.Controls.Primitives.FlyoutBase.SetAttachedFlyout(anchor, flyout);
            flyout.Closed += (_, _) =>
            {
                editor.Dispose(); editor.DataContext = null; flyout.Content = null;
                if (ReferenceEquals(_keyFlyout, flyout)) _keyFlyout = null;
                if (ReferenceEquals(Avalonia.Controls.Primitives.FlyoutBase.GetAttachedFlyout(anchor), flyout)) Avalonia.Controls.Primitives.FlyoutBase.SetAttachedFlyout(anchor, null);
            };
            flyout.ShowAt(anchor, true);
        };
        menu.Items.Add(new Separator()); menu.Items.Add(precise);
        Avalonia.Controls.Primitives.FlyoutBase.SetAttachedFlyout(anchor, menu);
        menu.Closed += (_, _) => { if (ReferenceEquals(Avalonia.Controls.Primitives.FlyoutBase.GetAttachedFlyout(anchor), menu)) Avalonia.Controls.Primitives.FlyoutBase.SetAttachedFlyout(anchor, null); };
        menu.ShowAt(anchor, true);
    }
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e) { _objectFlyout?.Hide(); _keyFlyout?.Hide(); base.OnDetachedFromVisualTree(e); }
}
