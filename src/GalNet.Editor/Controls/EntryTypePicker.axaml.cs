using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GalNet.Core.Entry;
using GalNet.Editor.Commands;
using GalNet.Editor.Models.Graph;

namespace GalNet.Editor.Controls;

public sealed record EntryTypeLeafItem(string Value, string DisplayNameKey, string GestureText);
public sealed record EntryTypeCategoryItem(string DisplayNameKey, IReadOnlyList<EntryTypeLeafItem> Items);

public partial class EntryTypePicker : UserControl
{
    public static readonly StyledProperty<IEnumerable?> OptionsProperty =
        AvaloniaProperty.Register<EntryTypePicker, IEnumerable?>(nameof(Options));
    public static readonly StyledProperty<string> SelectedValueProperty =
        AvaloniaProperty.Register<EntryTypePicker, string>(nameof(SelectedValue), TextEntry.TypeId, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<EditorShortcutService?> ShortcutServiceProperty =
        AvaloniaProperty.Register<EntryTypePicker, EditorShortcutService?>(nameof(ShortcutService));
    public static readonly DirectProperty<EntryTypePicker, string> SelectedDisplayNameKeyProperty =
        AvaloniaProperty.RegisterDirect<EntryTypePicker, string>(nameof(SelectedDisplayNameKey), picker => picker.SelectedDisplayNameKey);
    public static readonly DirectProperty<EntryTypePicker, string> SelectedGestureTextProperty =
        AvaloniaProperty.RegisterDirect<EntryTypePicker, string>(nameof(SelectedGestureText), picker => picker.SelectedGestureText);

    public IEnumerable? Options { get => GetValue(OptionsProperty); set => SetValue(OptionsProperty, value); }
    public string SelectedValue { get => GetValue(SelectedValueProperty); set => SetValue(SelectedValueProperty, value); }
    public EditorShortcutService? ShortcutService { get => GetValue(ShortcutServiceProperty); set => SetValue(ShortcutServiceProperty, value); }

    public ObservableCollection<EntryTypeCategoryItem> Categories { get; } = [];
    private string _selectedDisplayNameKey = $"Entry.Type.{TextEntry.TypeId}";
    private string _selectedGestureText = "";
    private EditorShortcutService? _subscribedShortcutService;
    public string SelectedDisplayNameKey => _selectedDisplayNameKey;
    public string SelectedGestureText => _selectedGestureText;

    public EntryTypePicker()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        TypeTree.PointerReleased += OnTreePointerReleased;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShortcutServiceProperty)
        {
            SubscribeToShortcuts(change.NewValue as EditorShortcutService);
            RebuildCategories();
        }
        else if (change.Property == OptionsProperty)
            RebuildCategories();
        else if (change.Property == SelectedValueProperty)
            RaiseSelectedProperties();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToShortcuts(ShortcutService);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        SubscribeToShortcuts(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void SubscribeToShortcuts(EditorShortcutService? service)
    {
        if (ReferenceEquals(_subscribedShortcutService, service)) return;
        if (_subscribedShortcutService is not null)
            _subscribedShortcutService.ShortcutsChanged -= OnShortcutsChanged;
        _subscribedShortcutService = service;
        if (_subscribedShortcutService is not null)
            _subscribedShortcutService.ShortcutsChanged += OnShortcutsChanged;
    }

    private void OnShortcutsChanged()
    {
        RebuildCategories();
        RaiseSelectedProperties();
    }

    private void RebuildCategories()
    {
        Categories.Clear();
        foreach (var group in GetOptions().GroupBy(item => item.Category))
        {
            Categories.Add(new EntryTypeCategoryItem(
                $"Entry.Category.{group.Key}",
                group.Select(item => new EntryTypeLeafItem(
                    item.Value,
                    item.DisplayNameKey,
                    FindTypeCommand(item.Value)?.Gesture?.ToString() ?? "")).ToArray()));
        }
        RaiseSelectedProperties();
    }

    private IReadOnlyList<EntryTypeOption> GetOptions() => Options?.Cast<EntryTypeOption>().ToArray() ?? [];
    private EntryTypeShortcutCommand? FindTypeCommand(string type) =>
        ShortcutService?.Commands.OfType<EntryTypeShortcutCommand>().FirstOrDefault(command => command.EntryType == type);

    private void OnPopupOpened(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => TypeTree.Focus(), DispatcherPriority.Input);

    private void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e) => CommitSelectedTreeItem();

    private bool CommitSelectedTreeItem()
    {
        if (TypeTree.SelectedItem is not EntryTypeLeafItem leaf) return false;
        SelectedValue = leaf.Value;
        PickerPopup.IsOpen = false;
        TypeTree.SelectedItem = null;
        Focus();
        return true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (PickerPopup.IsOpen && e.Key == Key.Escape)
        {
            PickerPopup.IsOpen = false;
            Focus();
            e.Handled = true;
            return;
        }
        if (PickerPopup.IsOpen && e.Key == Key.Enter && CommitSelectedTreeItem())
        {
            e.Handled = true;
            return;
        }
        if (ShortcutService?.FindByGesture(new KeyGesture(e.Key, e.KeyModifiers), "EntryTypePicker") is not EntryTypeShortcutCommand command)
            return;
        SelectedValue = command.EntryType;
        PickerPopup.IsOpen = false;
        e.Handled = true;
    }

    private void RaiseSelectedProperties()
    {
        SetAndRaise(SelectedDisplayNameKeyProperty, ref _selectedDisplayNameKey,
            GetOptions().FirstOrDefault(item => item.Value == SelectedValue)?.DisplayNameKey ?? $"Entry.Type.{SelectedValue}");
        SetAndRaise(SelectedGestureTextProperty, ref _selectedGestureText,
            FindTypeCommand(SelectedValue)?.Gesture?.ToString() ?? "");
    }
}
