using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Collections.Specialized;
using System.ComponentModel;
using SkiaSharp;

namespace GalNet.Rendering.Scene;

/// <summary>One scene surface: layers, renderables and scene effects are composed before any GameShell UI.</summary>
public sealed class SceneLayerHost : Control
{
    public static readonly StyledProperty<IEnumerable<SceneLayerItem>?> ItemsSourceProperty =
        AvaloniaProperty.Register<SceneLayerHost, IEnumerable<SceneLayerItem>?>(nameof(ItemsSource));
    public static readonly StyledProperty<IEnumerable<SceneEffectInstance>?> EffectsSourceProperty =
        AvaloniaProperty.Register<SceneLayerHost, IEnumerable<SceneEffectInstance>?>(nameof(EffectsSource));
    public static readonly StyledProperty<IEnumerable<ISceneRenderable>?> RenderablesSourceProperty =
        AvaloniaProperty.Register<SceneLayerHost, IEnumerable<ISceneRenderable>?>(nameof(RenderablesSource));

    private readonly Dictionary<SceneLayerItem, long> _insertionOrder = [];
    private readonly HashSet<SceneEffectInstance> _effectSubscriptions = [];
    private INotifyCollectionChanged? _layersCollection;
    private INotifyCollectionChanged? _effectsCollection;
    private long _nextInsertionOrder;
    private bool _renderFailureReported;
    private SceneRenderPlan _renderPlan = SceneRenderPlan.Empty;

    static SceneLayerHost()
    {
        ItemsSourceProperty.Changed.AddClassHandler<SceneLayerHost>((host, _) => host.ResetLayers());
        EffectsSourceProperty.Changed.AddClassHandler<SceneLayerHost>((host, _) => host.ResetEffects());
        RenderablesSourceProperty.Changed.AddClassHandler<SceneLayerHost>((host, _) => host.InvalidateVisual());
    }

    public IEnumerable<SceneLayerItem>? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public IEnumerable<SceneEffectInstance>? EffectsSource { get => GetValue(EffectsSourceProperty); set => SetValue(EffectsSourceProperty, value); }
    public IEnumerable<ISceneRenderable>? RenderablesSource { get => GetValue(RenderablesSourceProperty); set => SetValue(RenderablesSourceProperty, value); }
    public SceneRenderPlan RenderPlan => _renderPlan;

    /// <summary>Exports the exact scene graph used by the renderer, without GameShell UI.</summary>
    public byte[] CapturePng()
    {
        using var scene = SceneRenderPipeline.Render(_renderPlan, EffectsSource ?? [], RenderablesSource, Bounds.Size);
        using var image = SKImage.FromBitmap(scene);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        try
        {
            using var scene = SceneRenderPipeline.Render(_renderPlan, EffectsSource ?? [], RenderablesSource, Bounds.Size);
            using var image = SceneRenderPipeline.ToAvaloniaBitmap(scene);
            context.DrawImage(image, new Rect(image.Size), new Rect(Bounds.Size));
        }
        catch (Exception error)
        {
            if (!_renderFailureReported)
            {
                _renderFailureReported = true;
                System.Diagnostics.Trace.TraceWarning("Skia scene pipeline unavailable; rendering without texture effects. {0}", error.Message);
            }
            SceneLayerFallbackRenderer.Render(context, _renderPlan, Bounds.Size);
        }
    }

    private void ResetLayers()
    {
        if (_layersCollection is not null) _layersCollection.CollectionChanged -= OnLayersChanged;
        foreach (var item in _insertionOrder.Keys) item.PropertyChanged -= OnLayerChanged;
        _insertionOrder.Clear();
        _layersCollection = ItemsSource as INotifyCollectionChanged;
        if (_layersCollection is not null) _layersCollection.CollectionChanged += OnLayersChanged;
        if (ItemsSource is not null) foreach (var item in ItemsSource) AddLayer(item);
        RebuildPlan();
    }
    private void ResetEffects()
    {
        if (_effectsCollection is not null) _effectsCollection.CollectionChanged -= OnEffectsChanged;
        foreach (var effect in _effectSubscriptions) effect.PropertyChanged -= OnEffectChanged;
        _effectSubscriptions.Clear();
        _effectsCollection = EffectsSource as INotifyCollectionChanged;
        if (_effectsCollection is not null) _effectsCollection.CollectionChanged += OnEffectsChanged;
        if (EffectsSource is not null) foreach (var effect in EffectsSource) AddEffect(effect);
        InvalidateVisual();
    }
    private void OnLayersChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset) { ResetLayers(); return; }
        if (args.OldItems is not null) foreach (var item in args.OldItems.OfType<SceneLayerItem>()) RemoveLayer(item);
        if (args.NewItems is not null) foreach (var item in args.NewItems.OfType<SceneLayerItem>()) AddLayer(item);
        RebuildPlan();
    }
    private void OnEffectsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset) { ResetEffects(); return; }
        if (args.OldItems is not null) foreach (var effect in args.OldItems.OfType<SceneEffectInstance>()) RemoveEffect(effect);
        if (args.NewItems is not null) foreach (var effect in args.NewItems.OfType<SceneEffectInstance>()) AddEffect(effect);
        InvalidateVisual();
    }
    private void AddEffect(SceneEffectInstance effect)
    {
        if (_effectSubscriptions.Add(effect)) effect.PropertyChanged += OnEffectChanged;
    }
    private void RemoveEffect(SceneEffectInstance effect)
    {
        if (_effectSubscriptions.Remove(effect)) effect.PropertyChanged -= OnEffectChanged;
    }
    private void OnEffectChanged(object? sender, PropertyChangedEventArgs args) => InvalidateVisual();
    private void AddLayer(SceneLayerItem item)
    {
        if (!_insertionOrder.TryAdd(item, _nextInsertionOrder++)) return;
        item.PropertyChanged += OnLayerChanged;
    }
    private void RemoveLayer(SceneLayerItem item)
    {
        if (!_insertionOrder.Remove(item)) return;
        item.PropertyChanged -= OnLayerChanged;
    }
    private void OnLayerChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not SceneLayerItem item || !_insertionOrder.ContainsKey(item)) return;
        if (args.PropertyName == nameof(SceneLayerItem.Z)) RebuildPlan(); else InvalidateVisual();
    }
    private void RebuildPlan()
    {
        _renderPlan = SceneRenderPlan.Create(_insertionOrder.Select(pair => new SceneRenderEntry(pair.Key, pair.Value)));
        InvalidateVisual();
    }
}

/// <summary>Only used when a platform cannot execute the Skia texture graph.</summary>
internal static class SceneLayerFallbackRenderer
{
    public static void Render(DrawingContext context, SceneRenderPlan plan, Size surface)
    {
        foreach (var entry in plan.Items)
        {
            var item = entry.Layer;
            if (!item.IsVisible || item.Opacity <= 0) continue;
            using var opacity = context.PushOpacity(Math.Clamp(item.Opacity, 0, 1));
            if (!string.IsNullOrWhiteSpace(item.Color)) context.DrawRectangle(new SolidColorBrush(Color.Parse(item.Color)), null, new Rect(surface));
            else if (item.Texture is not null) context.DrawImage(item.Texture.AvaloniaImage, new Rect(item.Texture.Size), new Rect(surface));
        }
    }
}
