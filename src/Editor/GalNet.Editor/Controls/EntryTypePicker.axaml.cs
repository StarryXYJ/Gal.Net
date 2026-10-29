using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Commands;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;

namespace GalNet.Editor.Controls;

public sealed record EntryTypeLeafItem(string Value, string DisplayNameKey, string GestureText, string DisplayName);
public sealed record EntryTypeCategoryItem(string DisplayNameKey, IReadOnlyList<EntryTypeLeafItem> Items, string DisplayName);
public sealed class EntryTypeConfirmedEventArgs(string type) : EventArgs { public string Type { get; } = type; }

public partial class EntryTypePicker : UserControl
{
    public static readonly DirectProperty<EntryTypePicker, bool> IsButtonModeProperty =
        AvaloniaProperty.RegisterDirect<EntryTypePicker, bool>(nameof(IsButtonMode), picker => picker.IsButtonMode);
    public static readonly StyledProperty<string?> ButtonLabelProperty =
        AvaloniaProperty.Register<EntryTypePicker, string?>(nameof(ButtonLabel));
    public string? ButtonLabel { get => GetValue(ButtonLabelProperty); set => SetValue(ButtonLabelProperty, value); }
    public bool IsButtonMode => ButtonLabel is not null;

    public static readonly StyledProperty<IEnumerable?> OptionsProperty =
        AvaloniaProperty.Register<EntryTypePicker, IEnumerable?>(nameof(Options));
    public static readonly StyledProperty<string> SelectedValueProperty =
        AvaloniaProperty.Register<EntryTypePicker, string>(nameof(SelectedValue), BuiltinEntryIds.Text, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<EditorShortcutService?> ShortcutServiceProperty =
        AvaloniaProperty.Register<EntryTypePicker, EditorShortcutService?>(nameof(ShortcutService));
    public static readonly DirectProperty<EntryTypePicker, string> SelectedDisplayNameKeyProperty =
        AvaloniaProperty.RegisterDirect<EntryTypePicker, string>(nameof(SelectedDisplayNameKey), picker => picker.SelectedDisplayNameKey);
    public static readonly DirectProperty<EntryTypePicker, string> SelectedGestureTextProperty =
        AvaloniaProperty.RegisterDirect<EntryTypePicker, string>(nameof(SelectedGestureText), picker => picker.SelectedGestureText);
    public static readonly DirectProperty<EntryTypePicker, string> SelectedDisplayNameProperty = AvaloniaProperty.RegisterDirect<EntryTypePicker, string>(nameof(SelectedDisplayName), picker => picker.SelectedDisplayName);

    public IEnumerable? Options { get => GetValue(OptionsProperty); set => SetValue(OptionsProperty, value); }
    public string SelectedValue { get => GetValue(SelectedValueProperty); set => SetValue(SelectedValueProperty, value); }
    public EditorShortcutService? ShortcutService { get => GetValue(ShortcutServiceProperty); set => SetValue(ShortcutServiceProperty, value); }

    public ObservableCollection<EntryTypeCategoryItem> Categories { get; } = [];
    private string _selectedDisplayNameKey = $"Entry.Type.{BuiltinEntryIds.Text}";
    private string _selectedGestureText = "";
    private string _selectedDisplayName = BuiltinEntryIds.Text;
    public string SelectedDisplayName => _selectedDisplayName;
    private EditorShortcutService? _subscribedShortcutService;
    private IEditorLocalizationService? _localization;
    private InputElement? _returnFocus;
    public event EventHandler<EntryTypeConfirmedEventArgs>? TypeConfirmed;
    public event EventHandler? Opened;
    public event EventHandler? Closed;
    public bool IsOpen => PickerPopup.IsOpen;
    public IReadOnlyList<EntryTypeLeafItem> Results => Categories.SelectMany(category => category.Items).ToArray();
    public string SelectedDisplayNameKey => _selectedDisplayNameKey;
    public string SelectedGestureText => _selectedGestureText;

    public EntryTypePicker()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        PopupContent.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        TypeTree.PointerReleased += OnTreePointerReleased;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ButtonLabelProperty)
            RaisePropertyChanged(IsButtonModeProperty, change.OldValue is not null, change.NewValue is not null);
        else if (change.Property == ShortcutServiceProperty)
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
        _localization = EditorLocalizationService.Current;
        if (_localization is not null) _localization.PropertyChanged += OnLocaleChanged;
        RebuildCategories();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        SubscribeToShortcuts(null);
        if (_localization is not null) _localization.PropertyChanged -= OnLocaleChanged;
        _localization = null; PickerPopup.IsOpen = false;
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
        if (TypeTree is null) return;
        TypeTree.SelectedItem = null;
        Categories.Clear();
        var query = SearchInput.Text?.Trim() ?? "";
        foreach (var group in GetOptions().Where(item => Matches(item, query)).GroupBy(item => item.Category))
        {
            Categories.Add(new EntryTypeCategoryItem(
                $"Entry.Category.{group.Key}",
                group.Select(item => new EntryTypeLeafItem(
                    item.Value,
                    item.DisplayNameKey,
                    FindTypeCommand(item.Value)?.Gesture?.ToString() ?? "", Display(item))).ToArray(), Label($"Entry.Category.{group.Key}", group.Key)));
        }
        RaiseSelectedProperties();
    }
    private void OnSearchChanged(object? sender, TextChangedEventArgs args) => RebuildCategories();
    private void OnLocaleChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == "Item[]") RebuildCategories(); }
    private bool Matches(EntryTypeOption item, string query)
    {
        var label = Display(item);
        return query.Length == 0 || item.Value.Contains(query, StringComparison.OrdinalIgnoreCase) || label.Contains(query, StringComparison.OrdinalIgnoreCase)
            || item.SearchAliases?.Any(alias => alias.Contains(query, StringComparison.OrdinalIgnoreCase)) == true
            || SimplePinyin.GetValueOrDefault(item.Value, "").Split(' ').Any(alias => alias.Length > 0 && alias.Contains(query, StringComparison.OrdinalIgnoreCase));
    }
    private string Display(EntryTypeOption item)
    {
        return Label(item.DisplayNameKey, item.Value);
    }
    private string Label(string key, string fallback) { var label = _localization?[key]; return label is null || label == key || label == "[" + key + "]" || label == "#" + key + "#" ? fallback : label; }
    private static readonly IReadOnlyDictionary<string, string> SimplePinyin = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [BuiltinEntryIds.Text] = "duibai wenben db wb",
        [BuiltinEntryIds.ShowLayer] = "xianshi tuxiang xs tx",
        [BuiltinEntryIds.HideLayer] = "yincang yc",
        [BuiltinEntryIds.MoveLayer] = "yidong yd",
        ["flow.wait"] = "dengdai dd",
        ["animation.animate"] = "donghua dh",
        ["timeline.play"] = "shijianzhou sjz timeline",
        ["audio.play"] = "bofang yinpin yinyue bf yp yy",
        ["video.play"] = "shipin sp",
        ["effect.apply"] = "texiao tx",
        ["particle.play"] = "lizi lz",
        ["variable.set"] = "bianliang bl",
        ["gallery.unlock"] = "jiesuo js"
    };

    private EntryTypeOption[] GetOptions() => Options?.Cast<EntryTypeOption>().ToArray() ?? [];
    private EntryTypeShortcutCommand? FindTypeCommand(string type) =>
        ShortcutService?.Commands.OfType<EntryTypeShortcutCommand>().FirstOrDefault(command => command.EntryType == type);

    public void OpenAt(Control? target = null)
    {
        if (IsOpen) return;
        _returnFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as InputElement;
        PickerPopup.PlacementTarget = target ?? OpenButton;
        PickerPopup.IsOpen = true;
    }
    private void OnPopupOpened(object? sender, EventArgs e)
    {
        _returnFocus ??= OpenButton;
        SearchInput.Text = ""; RebuildCategories(); Opened?.Invoke(this, EventArgs.Empty);
        Dispatcher.UIThread.Post(() => { if (IsOpen) SearchInput.Focus(); }, DispatcherPriority.Input);
    }
    private void OnPopupClosed(object? sender, EventArgs e)
    {
        TypeTree.SelectedItem = null; PickerPopup.PlacementTarget = OpenButton;
        _returnFocus?.Focus(); _returnFocus = null; Closed?.Invoke(this, EventArgs.Empty);
    }

    private void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e) => CommitSelectedTreeItem();

    private bool CommitSelectedTreeItem()
    {
        if (TypeTree.SelectedItem is not EntryTypeLeafItem leaf) return false;
        SelectedValue = leaf.Value;
        TypeConfirmed?.Invoke(this, new(leaf.Value));
        PickerPopup.IsOpen = false;
        TypeTree.SelectedItem = null;
        return true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (IsOpen && SearchInput.GetVisualDescendants().OfType<TextPresenter>().Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText))) return;
        if (IsOpen && e.Key is Key.Up or Key.Down)
        {
            var results = Results; var index = results.ToList().FindIndex(item => ReferenceEquals(item, TypeTree.SelectedItem));
            if (results.Count > 0) TypeTree.SelectedItem = results[index < 0 ? 0 : Math.Clamp(index + (e.Key == Key.Up ? -1 : 1), 0, results.Count - 1)];
            TypeTree.UpdateLayout();
            TypeTree.GetVisualDescendants().OfType<TreeViewItem>().FirstOrDefault(item => ReferenceEquals(item.DataContext, TypeTree.SelectedItem))?.BringIntoView();
            e.Handled = true; return;
        }
        if (PickerPopup.IsOpen && e.Key == Key.Escape)
        {
            PickerPopup.IsOpen = false;
            e.Handled = true;
            return;
        }
        if (PickerPopup.IsOpen && e.Key == Key.Enter && CommitSelectedTreeItem())
        {
            e.Handled = true;
            return;
        }
        if (IsOpen) return;
        if (ShortcutService?.FindByGesture(new KeyGesture(e.Key, e.KeyModifiers), "EntryTypePicker") is not EntryTypeShortcutCommand command)
            return;
        SelectedValue = command.EntryType;
        TypeConfirmed?.Invoke(this, new(command.EntryType));
        PickerPopup.IsOpen = false;
        e.Handled = true;
    }

    private void RaiseSelectedProperties()
    {
        SetAndRaise(SelectedDisplayNameProperty, ref _selectedDisplayName, GetOptions().FirstOrDefault(item => item.Value == SelectedValue) is { } selected ? Display(selected) : SelectedValue);
        SetAndRaise(SelectedDisplayNameKeyProperty, ref _selectedDisplayNameKey,
            GetOptions().FirstOrDefault(item => item.Value == SelectedValue)?.DisplayNameKey ?? $"Entry.Type.{SelectedValue}");
        SetAndRaise(SelectedGestureTextProperty, ref _selectedGestureText,
            FindTypeCommand(SelectedValue)?.Gesture?.ToString() ?? "");
    }
}
