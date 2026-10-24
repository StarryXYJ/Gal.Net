using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using GalNet.Editor.Controls;
using GalNet.Editor.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Views;

public partial class TimelinePanelView
{
    private Flyout? _eventFlyout;
    private Func<string, bool>? _eventInsertion;
    private void OnEventTypeOpened(object? sender, EventArgs args)
    { if (DataContext is ViewModels.TimelinePanelViewModel vm) _eventInsertion = vm.Session.CaptureEventInsertion(); }
    private void OnEventTypeClosed(object? sender, EventArgs args) => _eventInsertion = null;
    private void OnEventTypeConfirmed(object? sender, EntryTypeConfirmedEventArgs args) => _eventInsertion?.Invoke(args.Type);
    private void OnEventPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (sender is not Control { DataContext: TimelineEventRow row } || DataContext is not ViewModels.TimelinePanelViewModel vm) return;
        if (args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) vm.Session.SelectEvent(row, args.KeyModifiers.HasFlag(KeyModifiers.Control) || args.KeyModifiers.HasFlag(KeyModifiers.Meta));
    }
    private void OnEditEvent(object? sender, RoutedEventArgs args)
    { if (sender is Control { DataContext: TimelineEventRow row } control) OpenEventEditor(control, row); }
    private void OpenEventEditor(Control anchor, TimelineEventRow row)
    {
        _eventFlyout?.Hide();
        if (DataContext is not ViewModels.TimelinePanelViewModel vm || vm.Session.BeginEventEdit(row) is not { } draft) return;
        var host = new DtoEditorHost { Name = "TimelineEventForm", Item = draft.Item, Registry = vm.FieldEditors };
        var apply = new Button { Name = "ApplyTimelineEvent", Content = vm.Session.SceneText["Timeline.Event.Apply"] };
        var cancel = new Button { Name = "CancelTimelineEvent", Content = vm.Session.SceneText["Timeline.Event.Cancel"] };
        var body = new StackPanel { Width = 520, Margin = new(10), Spacing = 8 };
        body.Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://GalNet.Editor/")) { Source = new Uri("avares://GalNet.Editor/Controls/AuthorFormStyles.axaml") });
        var heading = new TextBlock { Text = row.Label, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        body.Children.Add(heading);
        body.Children.Add(new ScrollViewer { MaxHeight = 450, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = host });
        body.Children.Add(new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { apply, cancel } });
        var flyout = new Flyout { Content = body }; _eventFlyout = flyout;
        FlyoutBase.SetAttachedFlyout(anchor, flyout);
        void Changed(object? sender, PropertyChangedEventArgs args) { if (!draft.IsCurrent) flyout.Hide(); }
        vm.Session.PropertyChanged += Changed;
        void LocaleChanged(object? sender, PropertyChangedEventArgs args)
        { heading.Text = row.Label; apply.Content = vm.Session.SceneText["Timeline.Event.Apply"]; cancel.Content = vm.Session.SceneText["Timeline.Event.Cancel"]; }
        vm.Session.SceneText.PropertyChanged += LocaleChanged;
        apply.Click += (_, _) => { if (draft.Apply()) flyout.Hide(); };
        cancel.Click += (_, _) => flyout.Hide();
        flyout.Closed += (_, _) =>
        {
            vm.Session.PropertyChanged -= Changed; vm.Session.SceneText.PropertyChanged -= LocaleChanged; host.Item = null; host.Registry = null; flyout.Content = null;
            if (ReferenceEquals(vm.Session.EventEdit, draft)) vm.Session.EndEventEdit(); else draft.Dispose();
            if (ReferenceEquals(_eventFlyout, flyout)) _eventFlyout = null;
            if (ReferenceEquals(FlyoutBase.GetAttachedFlyout(anchor), flyout)) FlyoutBase.SetAttachedFlyout(anchor, null);
        };
        flyout.ShowAt(anchor);
    }
    private void OnEventContextRequested(object? sender, ContextRequestedEventArgs args)
    {
        if (sender is not Control { DataContext: TimelineEventRow row } control || DataContext is not ViewModels.TimelinePanelViewModel vm) return;
        if (!vm.Session.EventCanvas.Selection.Contains(row.Id)) vm.Session.SelectEvent(row);
        ShowEventMenu(control); args.Handled = true;
    }
    private void OnEventKeyContextRequested(object? sender, GalNet.Game.Controls.Timeline.TimelineKeyContextEventArgs args)
    { if (sender is Control control) ShowEventMenu(control); }
    private void ShowEventMenu(Control anchor)
    {
        if (DataContext is not ViewModels.TimelinePanelViewModel vm) return;
        var rows = vm.Session.Events.Where(row => row.IsSelected).ToArray(); if (rows.Length == 0) return;
        var menu = new MenuFlyout();
        void Add(string name, string key, Action action)
        { var item = new MenuItem { Name = name, Header = vm.Session.SceneText[key] }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        if (rows.Length == 1)
        {
            if (rows[0].CanEdit) Add("EditTimelineEventMenu", "Timeline.Event.Edit", () => OpenEventEditor(anchor, rows[0]));
            Add("EarlierTimelineEvent", "Timeline.Event.Earlier", () => vm.Session.OrderEvent(rows[0], -1));
            Add("LaterTimelineEvent", "Timeline.Event.Later", () => vm.Session.OrderEvent(rows[0], 1));
        }
        Add("DeleteTimelineEvents", "Timeline.Event.Delete", vm.Session.DeleteSelectedEvents);
        FlyoutBase.SetAttachedFlyout(anchor, menu);
        menu.Closed += (_, _) => { if (ReferenceEquals(FlyoutBase.GetAttachedFlyout(anchor), menu)) FlyoutBase.SetAttachedFlyout(anchor, null); };
        menu.ShowAt(anchor, true);
    }
}
