using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GalNet.Game.Controls;

/// <summary>
/// Canvas host for application-provided visual scene layers. It owns the actual visual
/// children so layer coordinates and z-order always target the arranged controls rather
/// than an intermediary ItemsControl container.
/// </summary>
public class SceneLayerHost : Canvas
{
    public static readonly StyledProperty<IEnumerable<SceneLayerItem>?> ItemsSourceProperty =
        AvaloniaProperty.Register<SceneLayerHost, IEnumerable<SceneLayerItem>?>(nameof(ItemsSource));

    private readonly Dictionary<SceneLayerItem, ContentPresenter> _presenters = [];
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
        foreach (var item in _presenters.Keys)
            item.PropertyChanged -= OnItemPropertyChanged;

        _presenters.Clear();
        Children.Clear();
        _collection = ItemsSource as INotifyCollectionChanged;
        if (_collection is not null)
            _collection.CollectionChanged += OnCollectionChanged;

        if (ItemsSource is null) return;
        foreach (var item in ItemsSource)
            AddItem(item);
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs) => ResetItems();

    private void AddItem(SceneLayerItem item)
    {
        var presenter = new ContentPresenter
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        _presenters.Add(item, presenter);
        item.PropertyChanged += OnItemPropertyChanged;
        Apply(item, presenter);
        Children.Add(presenter);
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (sender is SceneLayerItem item && _presenters.TryGetValue(item, out var presenter))
            Apply(item, presenter);
    }

    private static void Apply(SceneLayerItem item, ContentPresenter presenter)
    {
        presenter.Content = item.Content;
        presenter.IsVisible = item.IsVisible;
        SetLeft(presenter, item.X);
        SetTop(presenter, item.Y);
        presenter.SetValue(ZIndexProperty, item.ZIndex);
    }
}

/// <summary>Minimal bindable layer item used by the default <see cref="SceneLayerHost"/> template.</summary>
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
