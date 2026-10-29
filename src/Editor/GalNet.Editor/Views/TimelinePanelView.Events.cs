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
            Add("EarlierTimelineEvent", "Timeline.Event.Earlier", () => vm.Session.OrderEvent(rows[0], -1));
            Add("LaterTimelineEvent", "Timeline.Event.Later", () => vm.Session.OrderEvent(rows[0], 1));
        }
        Add("DeleteTimelineEvents", "Timeline.Event.Delete", vm.Session.DeleteSelectedEvents);
        FlyoutBase.SetAttachedFlyout(anchor, menu);
        menu.Closed += (_, _) => { if (ReferenceEquals(FlyoutBase.GetAttachedFlyout(anchor), menu)) FlyoutBase.SetAttachedFlyout(anchor, null); };
        menu.ShowAt(anchor, true);
    }
}
