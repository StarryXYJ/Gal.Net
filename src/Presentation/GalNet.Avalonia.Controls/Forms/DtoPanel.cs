using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using GalNet.Core.Parameters;

namespace GalNet.Game.Controls.Forms;

/// <summary>Ordered DTO controls keyed by item identity, including repeated DTO types.</summary>
public sealed class DtoPanel : StackPanel
{
    public static readonly StyledProperty<IEnumerable<DtoEditorItem>?> ItemsSourceProperty = AvaloniaProperty.Register<DtoPanel, IEnumerable<DtoEditorItem>?>(nameof(ItemsSource));
    public static readonly StyledProperty<EditorRegistry?> RegistryProperty = AvaloniaProperty.Register<DtoPanel, EditorRegistry?>(nameof(Registry));
    public static readonly StyledProperty<ControlTheme?> FormThemeProperty = AvaloniaProperty.Register<DtoPanel, ControlTheme?>(nameof(FormTheme));
    public static readonly DirectProperty<DtoPanel, IReadOnlyList<ValueIssue>> DiagnosticsProperty = AvaloniaProperty.RegisterDirect<DtoPanel, IReadOnlyList<ValueIssue>>(nameof(Diagnostics), panel => panel.Diagnostics);
    private IReadOnlyList<ValueIssue> _diagnostics = [];
    private INotifyCollectionChanged? _subscribed;
    private bool _mounted;
    public IEnumerable<DtoEditorItem>? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public EditorRegistry? Registry { get => GetValue(RegistryProperty); set => SetValue(RegistryProperty, value); }
    public ControlTheme? FormTheme { get => GetValue(FormThemeProperty); set => SetValue(FormThemeProperty, value); }
    public IReadOnlyList<ValueIssue> Diagnostics => _diagnostics;
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _mounted = true; Subscribe(); RefreshItems(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { _mounted = false; Unsubscribe(); Children.Clear(); base.OnDetachedFromVisualTree(e); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (!_mounted) return;
        if (change.Property == ItemsSourceProperty) Subscribe();
        if (change.Property == ItemsSourceProperty || change.Property == RegistryProperty || change.Property == FormThemeProperty) RefreshItems();
    }
    private void Unsubscribe() { if (_subscribed is not null) _subscribed.CollectionChanged -= OnItemsChanged; _subscribed = null; }
    private void Subscribe() { Unsubscribe(); _subscribed = ItemsSource as INotifyCollectionChanged; if (_subscribed is not null) _subscribed.CollectionChanged += OnItemsChanged; }
    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs args) => RefreshItems();
    public void RefreshItems()
    {
        if (!_mounted) return;
        var items = ItemsSource?.ToArray() ?? [];
        if (items.Any(item => item is null) || items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != items.Length)
        { SetAndRaise(DiagnosticsProperty, ref _diagnostics, new[] { new ValueIssue("dto.identity", "DTO item IDs must be non-null and unique.") }); return; }
        var existing = Children.OfType<DtoEditorHost>().Where(host => host.Item is not null).ToDictionary(host => host.Item!.Id, StringComparer.Ordinal);
        var desired = items.Select(item => existing.GetValueOrDefault(item.Id) ?? new DtoEditorHost()).ToArray();
        foreach (var host in Children.OfType<DtoEditorHost>().Where(host => !desired.Contains(host)).ToArray()) Children.Remove(host);
        for (var index = 0; index < items.Length; index++)
        {
            var host = desired[index];
            host.Registry = Registry; host.FormTheme = FormTheme; host.Item = items[index];
            var previous = Children.IndexOf(host);
            if (previous < 0) Children.Insert(index, host); else if (previous != index) Children.Move(previous, index);
        }
        SetAndRaise(DiagnosticsProperty, ref _diagnostics, Array.Empty<ValueIssue>());
    }
}
