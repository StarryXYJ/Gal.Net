using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Controls;

public partial class EntryListEditorControl : ReorderableListControl
{
    private GroupEditorPanelViewModel? _subscribed;
    private string? _insertAnchor;
    private bool _quickInsert;
    public EntryListEditorControl()
    {
        InitializeComponent();
        InitializeDragDrop(ItemsListBox);
        AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
        ItemsListBox.SelectionChanged += (_, _) => ScrollSelectionIntoView();
        ItemsListBox.AddHandler(PointerPressedEvent, OnListBackgroundPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        DataContextChanged += (_, _) => Subscribe();
        AttachedToVisualTree += (_, _) => Subscribe();
        DetachedFromVisualTree += (_, _) => { if (_subscribed is not null) _subscribed.TypeSelectionRequested -= OpenInsertPicker; _subscribed = null; };
    }
    private void Subscribe()
    {
        if (_subscribed is not null) _subscribed.TypeSelectionRequested -= OpenInsertPicker;
        _subscribed = DataContext as GroupEditorPanelViewModel;
        if (_subscribed is not null) _subscribed.TypeSelectionRequested += OpenInsertPicker;
    }
    private void OnListBackgroundPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed || args.Source is not Control source) return;
        if (source.GetSelfAndVisualAncestors().Any(control => control is ListBoxItem or ScrollBar)) return;
        if (DataContext is GroupEditorPanelViewModel vm) vm.SelectedEntry = null;
        ItemsListBox.Focus();
    }
    private void OpenInsertPicker()
    {
        if (InsertPicker.IsOpen) return;
        _quickInsert = true;
        Control target = ItemsListBox;
        if (DataContext is GroupEditorPanelViewModel { SelectedEntry: { } entry })
        {
            ItemsListBox.ScrollIntoView(entry); ItemsListBox.UpdateLayout();
            target = ItemsListBox.ContainerFromItem(entry) ?? ItemsListBox;
        }
        else if (ItemsListBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroll)
            scroll.Offset = new Avalonia.Vector(scroll.Offset.X, System.Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
        InsertPicker.OpenAt(target);
    }
    private void OnInsertPickerOpened(object? sender, System.EventArgs args) => _insertAnchor = (DataContext as GroupEditorPanelViewModel)?.PickerAnchor;
    private void OnInsertPickerClosed(object? sender, System.EventArgs args)
    {
        if (_insertAnchor is not null && DataContext is GroupEditorPanelViewModel vm && !vm.GroupNode.Entries.Any(entry => entry.StableId == _insertAnchor))
            vm.ErrorText = GalNet.Editor.Services.EditorLocalizationService.Current?["Entry.AnchorMissing"] ?? "The insertion target was removed.";
        (DataContext as GroupEditorPanelViewModel)?.CancelInsertion();
        if (_quickInsert) ItemsListBox.Focus(); _quickInsert = false;
    }
    private void OnInsertTypeConfirmed(object? sender, EntryTypeConfirmedEventArgs args)
    {
        if (DataContext is GroupEditorPanelViewModel vm) vm.InsertType(args.Type, _insertAnchor);
        ScrollSelectionIntoView();
    }
    private void OnEntryTypeConfirmed(object? sender, EntryTypeConfirmedEventArgs args)
    {
        if (sender is Control { DataContext: EntryEditorItemViewModel entry } && DataContext is GroupEditorPanelViewModel vm) vm.ChangeType(entry, args.Type);
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || DataContext is not GroupEditorPanelViewModel viewModel || IsTextEditing(e.Source))
            return;

        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None) { OpenInsertPicker(); e.Handled = true; return; }
        if (e.Key != Key.A) return;

        if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
            viewModel.AppendEntryCommand.Execute(null);
        else if (e.KeyModifiers == KeyModifiers.Shift)
            viewModel.AddEntryCommand.Execute(null);
        else
            return;

        ScrollSelectionIntoView();
        e.Handled = true;
    }

    private static bool IsTextEditing(object? source)
    {
        if (source is not Avalonia.Controls.Control control) return false;
        return control.GetSelfAndVisualAncestors().Any(ancestor => ancestor is TextBox or NumericUpDown or Button or ComboBox or EntryTypePicker or ConditionEditorControl);
    }

    private void ScrollSelectionIntoView()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is GroupEditorPanelViewModel { SelectedEntry: { } entry })
                ItemsListBox.ScrollIntoView(entry);
        }, DispatcherPriority.Loaded);
    }

    private void OnAutocompleteLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.Control { DataContext: EntryParameterEditorItemViewModel field }
            && field.Id == "speaker"
            && DataContext is GroupEditorPanelViewModel viewModel)
            viewModel.CommitSpeaker(field.StringValue);
    }
}
