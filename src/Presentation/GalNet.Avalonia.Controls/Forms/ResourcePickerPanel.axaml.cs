using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace GalNet.Game.Controls.Forms;

/// <summary>Opaque identity; thumbnail ownership stays with the host.</summary>
public sealed record ResourcePickerItem(string Id, string Label, string Detail, string Icon = "▣", IImage? Thumbnail = null, string Badge = "")
{ public bool HasThumbnail => Thumbnail is not null; }

/// <summary>Assets and scene instances share search, cards, keyboard navigation and confirmation.</summary>
public partial class ResourcePickerPanel : UserControl
{
    private IReadOnlyList<ResourcePickerItem> _items = [];
    private bool _refreshing;
    public IReadOnlyList<ResourcePickerItem> Results { get; private set; } = [];
    public ResourcePickerItem? Selected => ResourceList.SelectedItem as ResourcePickerItem;
    public TextBox Search => SearchBox;
    public ListBox List => ResourceList;
    private string _hint = "";
    public string Hint { get => _hint; set { _hint = value; UpdateDetail(); } }
    public event Action? SelectionChanged;
    public event Action? Confirmed;
    public event Action? Cancelled;
    public ResourcePickerPanel() { InitializeComponent(); AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel); }
    public void SetItems(IReadOnlyList<ResourcePickerItem> items, string? selectedId = null)
    {
        if (items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != items.Count) throw new ArgumentException("Duplicate picker identities.", nameof(items));
        _items = items; Filter(selectedId ?? Selected?.Id);
    }
    private void OnSearchChanged(object? sender, TextChangedEventArgs args) => Filter(null);
    private void Filter(string? id)
    {
        _refreshing = true;
        try
        {
            var query = SearchBox.Text?.Trim() ?? "";
            Results = _items.Where(item => query.Length == 0 || item.Label.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Detail.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            ResourceList.ItemsSource = Results; ResourceList.SelectedItem = Results.FirstOrDefault(item => item.Id == id);
        }
        finally { _refreshing = false; }
        UpdateDetail(); SelectionChanged?.Invoke();
    }
    private void UpdateDetail() => DetailLabel.Text = Selected is { } item ? item.Detail + (Hint.Length == 0 ? "" : "\n" + Hint) : Hint;
    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs args) { UpdateDetail(); if (!_refreshing) SelectionChanged?.Invoke(); }
    private void OnReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (args.InitialPressMouseButton == MouseButton.Left && args.Source is Avalonia.Visual visual && (visual as ListBoxItem ?? visual.FindAncestorOfType<ListBoxItem>())?.DataContext is ResourcePickerItem && Selected is not null) Confirmed?.Invoke();
    }
    private void OnKey(object? sender, KeyEventArgs args)
    {
        if (SearchBox.GetVisualDescendants().OfType<TextPresenter>().Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText))) return;
        if (args.Key is Key.Up or Key.Down)
        {
            if (Results.Count > 0) { var index = ResourceList.SelectedIndex; ResourceList.SelectedIndex = index < 0 ? 0 : Math.Clamp(index + (args.Key == Key.Down ? 1 : -1), 0, Results.Count - 1); ResourceList.ScrollIntoView(ResourceList.SelectedItem!); }
        }
        else if (args.Key == Key.Enter) { if (Selected is not null) Confirmed?.Invoke(); }
        else if (args.Key == Key.Escape) Cancelled?.Invoke();
        else return;
        args.Handled = true;
    }
}
