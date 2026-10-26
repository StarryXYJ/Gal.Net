using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Controls;

public partial class EntryListEditorControl : ReorderableListControl
{
    public static readonly Avalonia.StyledProperty<bool> UseCommonFieldsProperty = Avalonia.AvaloniaProperty.Register<EntryListEditorControl, bool>(nameof(UseCommonFields));
    public bool UseCommonFields { get => GetValue(UseCommonFieldsProperty); set => SetValue(UseCommonFieldsProperty, value); }
    private GroupEditorPanelViewModel? _subscribed;
    private string? _insertAnchor;
    private bool _quickInsert;
    private bool _clipboardBusy;
    public EntryListEditorControl()
    {
        InitializeComponent();
        InitializeDragDrop(ItemsListBox);
        AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
        ItemsListBox.SelectionChanged += (_, args) =>
        {
            if (DataContext is GroupEditorPanelViewModel vm) vm.UpdateActiveSelection(args.AddedItems.OfType<EntryEditorItemViewModel>().LastOrDefault());
            ScrollSelectionIntoView();
        };
        ItemsListBox.ContextRequested += OnEntriesContextRequested;
        ItemsListBox.AddHandler(PointerPressedEvent, OnEntryPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        ItemsListBox.AddHandler(PointerPressedEvent, OnListBackgroundPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        DataContextChanged += (_, _) => Subscribe();
        AttachedToVisualTree += (_, _) => Subscribe();
        DetachedFromVisualTree += (_, _) => { if (_subscribed is not null) _subscribed.TypeSelectionRequested -= OpenInsertPicker; _subscribed = null; };
    }
    protected override void PrepareReorder(object item)
    {
        if (DataContext is GroupEditorPanelViewModel vm && item is EntryEditorItemViewModel entry && !vm.SelectedEntries.Contains(entry)) vm.SetSelection([entry]);
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
    private void OnEntryPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(ItemsListBox).Properties.IsLeftButtonPressed
            || args.KeyModifiers.HasFlag(KeyModifiers.Control)
            || args.KeyModifiers.HasFlag(KeyModifiers.Shift)
            || args.Source is not Control source
            || DataContext is not GroupEditorPanelViewModel vm)
            return;
        if (source.GetSelfAndVisualAncestors().OfType<ToggleButton>().Any(button => button.Name == "ParametersToggle")) return;

        if (source.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()?.DataContext is EntryEditorItemViewModel entry
            && !vm.SelectedEntries.Contains(entry))
            vm.SetSelection([entry]);
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
    private async void OnEntryTypeConfirmed(object? sender, EntryTypeConfirmedEventArgs args)
    {
        if (sender is Control { DataContext: EntryEditorItemViewModel entry }) await RunEntryAction(vm => vm.ChangeTypeAsync(entry, args.Type));
    }

    private async void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || DataContext is not GroupEditorPanelViewModel viewModel || IsTextEditing(e.Source))
            return;

        if (e.KeyModifiers == KeyModifiers.Control && e.Key is Key.C or Key.X or Key.V or Key.D or Key.A)
        {
            e.Handled = true;
            if (e.Key == Key.C) await CopySelectionAsync();
            else if (e.Key == Key.X) await CopySelectionAsync(cut: true);
            else if (e.Key == Key.V) await PasteSelectionAsync();
            else if (e.Key == Key.D) await RunEntryAction(vm => vm.DuplicateSelectionAsync());
            else ItemsListBox.SelectAll();
            return;
        }
        if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            await RunEntryAction(vm => vm.DeleteSelectionAsync());
            return;
        }
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            viewModel.SetSelection([]); e.Handled = true; return;
        }

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

    public Task CopySelectionAsync(bool cut = false) => RunEntryAction(async vm =>
    {
        var ids = vm.SelectedEntries.Select(entry => entry.StableId).ToArray();
        var text = vm.CopySelection();
        if (text.Length == 0) return;
        await WriteClipboardAsync(text);
        // A failed clipboard write must never remove the originals. A late completion
        // after the page/project closes must not modify a replacement project.
        if (cut && !vm.IsDisposed && ReferenceEquals(DataContext, vm)) await vm.DeleteCopiedEntriesAsync(ids, text);
    });

    public Task PasteSelectionAsync() => RunEntryAction(async vm =>
    {
        var anchor = vm.SelectedEntry?.StableId;
        var text = await ReadClipboardAsync();
        if (!vm.IsDisposed && ReferenceEquals(DataContext, vm)) await vm.PasteAsync(text ?? "", anchor);
    });

    protected virtual Task WriteClipboardAsync(string text) => (TopLevel.GetTopLevel(this)?.Clipboard
        ?? throw new EditorOperationException("entry.clipboardUnavailable", "Clipboard is unavailable.")).SetTextAsync(text);
    protected virtual Task<string?> ReadClipboardAsync() => (TopLevel.GetTopLevel(this)?.Clipboard
        ?? throw new EditorOperationException("entry.clipboardUnavailable", "Clipboard is unavailable.")).TryGetTextAsync();

    private async Task RunEntryAction(Func<GroupEditorPanelViewModel, Task> action)
    {
        if (_clipboardBusy || DataContext is not GroupEditorPanelViewModel vm || vm.IsDisposed) return;
        _clipboardBusy = true;
        try { vm.ErrorText = ""; await action(vm); ScrollSelectionIntoView(); }
        catch (Exception error)
        {
            if (!vm.IsDisposed && ReferenceEquals(DataContext, vm))
            {
                vm.ReportError(error);
            }
        }
        finally { _clipboardBusy = false; }
    }

    private void OnEntriesContextRequested(object? sender, ContextRequestedEventArgs args)
    {
        if (DataContext is not GroupEditorPanelViewModel vm || IsTextEditing(args.Source)) return;
        var row = (args.Source as Control)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        if (row?.DataContext is EntryEditorItemViewModel entry && !vm.SelectedEntries.Contains(entry)) vm.SetSelection([entry]);
        else if (row is null && args.TryGetPosition(ItemsListBox, out _)) vm.SetSelection([]);
        var menu = new MenuFlyout();
        void Add(string key, string gesture, Func<Task> action, bool enabled = true)
        {
            var item = new MenuItem { Header = vm.Workspace.Localize(key), InputGesture = KeyGesture.Parse(gesture), IsEnabled = enabled };
            item.Click += async (_, _) => await action(); menu.Items.Add(item);
        }
        var selected = vm.SelectedEntries.Count > 0;
        if (vm.SelectedEntry is { } target && vm.Workspace.ExternalDebug is { } debug)
        {
            var jump = new MenuItem { Header = vm.Workspace.Localize("Debug.Jump"), IsEnabled = debug.CanLocate };
            jump.Click += async (_, _) => await debug.SeekAsync(vm.GroupId, target.StableId);
            menu.Items.Add(jump);
            var breakpoint = new MenuItem { Header = vm.Workspace.Localize("Debug.Breakpoint"), IsEnabled = debug.CanSetBreakpoint };
            breakpoint.Click += async (_, _) => await debug.ToggleBreakpointAsync(vm.GroupId, target.StableId);
            menu.Items.Add(breakpoint);
        }
        Add("Entry.Menu.Copy", "Ctrl+C", () => CopySelectionAsync(), selected);
        Add("Entry.Menu.Cut", "Ctrl+X", () => CopySelectionAsync(cut: true), selected);
        Add("Entry.Menu.Paste", "Ctrl+V", PasteSelectionAsync);
        Add("Entry.Menu.Duplicate", "Ctrl+D", () => RunEntryAction(model => model.DuplicateSelectionAsync()), selected);
        Add("Entry.Menu.Delete", "Delete", () => RunEntryAction(model => model.DeleteSelectionAsync()), selected);
        menu.Items.Add(new Separator());
        Add("Entry.Menu.SelectAll", "Ctrl+A", () => { ItemsListBox.SelectAll(); return Task.CompletedTask; });
        menu.ShowAt((Control?)row ?? ItemsListBox); args.Handled = true;
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

}
