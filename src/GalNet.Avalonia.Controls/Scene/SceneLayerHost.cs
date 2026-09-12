using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Collections.Specialized;
using System.ComponentModel;

namespace GalNet.Game.Controls.Scene;

/// <summary>Single scene presentation surface that turns Layer data into one stable draw plan.</summary>
public sealed class SceneLayerHost : Control
{
    public static readonly StyledProperty<IEnumerable<SceneLayerItem>?> ItemsSourceProperty =
        AvaloniaProperty.Register<SceneLayerHost, IEnumerable<SceneLayerItem>?>(nameof(ItemsSource));

    private readonly Dictionary<SceneLayerItem, long> _insertionOrder = [];
    private INotifyCollectionChanged? _collection;
    private long _nextInsertionOrder;
    private SceneRenderPlan _renderPlan = SceneRenderPlan.Empty;

    static SceneLayerHost() => ItemsSourceProperty.Changed.AddClassHandler<SceneLayerHost>((host, _) => host.ResetItems());

    public IEnumerable<SceneLayerItem>? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>Stable scene ordering consumed by the current presenter and future GPU backend.</summary>
    public SceneRenderPlan RenderPlan => _renderPlan;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        foreach (var item in _renderPlan.Items)
            SceneLayerRenderer.Render(context, item.Layer, Bounds.Size);
    }

    private void ResetItems()
    {
        if (_collection is not null) _collection.CollectionChanged -= OnCollectionChanged;
        foreach (var item in _insertionOrder.Keys) item.PropertyChanged -= OnItemPropertyChanged;
        _insertionOrder.Clear();

        _collection = ItemsSource as INotifyCollectionChanged;
        if (_collection is not null) _collection.CollectionChanged += OnCollectionChanged;
        if (ItemsSource is not null)
            foreach (var item in ItemsSource) AddItem(item);
        RebuildRenderPlan();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (eventArgs.Action == NotifyCollectionChangedAction.Reset) { ResetItems(); return; }
        if (eventArgs.OldItems is not null)
            foreach (var item in eventArgs.OldItems.OfType<SceneLayerItem>()) RemoveItem(item);
        if (eventArgs.NewItems is not null)
            foreach (var item in eventArgs.NewItems.OfType<SceneLayerItem>()) AddItem(item);
        RebuildRenderPlan();
    }

    private void AddItem(SceneLayerItem item)
    {
        if (!_insertionOrder.TryAdd(item, _nextInsertionOrder++)) return;
        item.PropertyChanged += OnItemPropertyChanged;
    }

    private void RemoveItem(SceneLayerItem item)
    {
        if (!_insertionOrder.Remove(item)) return;
        item.PropertyChanged -= OnItemPropertyChanged;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (sender is not SceneLayerItem item || !_insertionOrder.ContainsKey(item)) return;
        if (eventArgs.PropertyName == nameof(SceneLayerItem.Z)) RebuildRenderPlan();
        else InvalidateVisual();
    }

    private void RebuildRenderPlan()
    {
        _renderPlan = SceneRenderPlan.Create(_insertionOrder.Select(pair => new SceneRenderEntry(pair.Key, pair.Value)));
        InvalidateVisual();
    }
}
