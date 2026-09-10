using Avalonia;
using Avalonia.Controls;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GalNet.Game.Controls;

/// <summary>
/// Canvas host for application-provided visual scene layers. It owns the actual layer
/// controls, matching the original player preview's direct Canvas composition.
/// </summary>
public class SceneLayerHost : Canvas
{
    public static readonly StyledProperty<IEnumerable<SceneLayerItem>?> ItemsSourceProperty =
        AvaloniaProperty.Register<SceneLayerHost, IEnumerable<SceneLayerItem>?>(nameof(ItemsSource));

    private readonly Dictionary<SceneLayerItem, Control> _controls = [];
    private INotifyCollectionChanged? _collection;

    static SceneLayerHost() => ItemsSourceProperty.Changed.AddClassHandler<SceneLayerHost>((host, _) => host.ResetItems());

    public IEnumerable<SceneLayerItem>? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    private void ResetItems()
    {
        if (_collection is not null)
            _collection.CollectionChanged -= OnCollectionChanged;
        foreach (var item in _controls.Keys.ToArray())
            RemoveItem(item);

        _collection = ItemsSource as INotifyCollectionChanged;
        if (_collection is not null)
            _collection.CollectionChanged += OnCollectionChanged;

        if (ItemsSource is null) return;
        foreach (var item in ItemsSource)
            AddItem(item);
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (eventArgs.Action == NotifyCollectionChangedAction.Reset)
        {
            ResetItems();
            return;
        }

        if (eventArgs.OldItems is not null)
            foreach (var item in eventArgs.OldItems.OfType<SceneLayerItem>())
                RemoveItem(item);

        if (eventArgs.NewItems is not null)
            foreach (var item in eventArgs.NewItems.OfType<SceneLayerItem>())
                AddItem(item);
    }

    private void AddItem(SceneLayerItem item)
    {
        if (item.Content is not { } control || _controls.ContainsKey(item)) return;

        _controls.Add(item, control);
        item.PropertyChanged += OnItemPropertyChanged;
        Apply(item, control);
        Children.Add(control);
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (sender is not SceneLayerItem item || !_controls.TryGetValue(item, out var control)) return;

        if (eventArgs.PropertyName == nameof(SceneLayerItem.Content) && !ReferenceEquals(control, item.Content))
        {
            RemoveItem(item);
            AddItem(item);
            return;
        }

        Apply(item, control);
    }

    private void RemoveItem(SceneLayerItem item)
    {
        if (!_controls.Remove(item, out var control)) return;

        item.PropertyChanged -= OnItemPropertyChanged;
        Children.Remove(control);
    }

    private static void Apply(SceneLayerItem item, Control control)
    {
        control.IsVisible = item.IsVisible;
        SetLeft(control, item.X);
        SetTop(control, item.Y);
        control.SetValue(ZIndexProperty, item.ZIndex);
    }
}

/// <summary>Bindable layer description whose content is attached directly to <see cref="SceneLayerHost"/>.</summary>
public sealed class SceneLayerItem : INotifyPropertyChanged
{
    private string _id = string.Empty;
    private Control? _content;
    private double _x;
    private double _y;
    private int _zIndex;
    private bool _isVisible = true;

    public string Id { get => _id; set => SetField(ref _id, value); }
    public Control? Content { get => _content; set => SetField(ref _content, value); }
    public double X { get => _x; set => SetField(ref _x, value); }
    public double Y { get => _y; set => SetField(ref _y, value); }
    public int ZIndex { get => _zIndex; set => SetField(ref _zIndex, value); }
    public bool IsVisible { get => _isVisible; set => SetField(ref _isVisible, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
