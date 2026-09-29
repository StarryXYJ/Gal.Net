using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
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
    public static readonly StyledProperty<SceneRenderBudget> RenderBudgetProperty =
        AvaloniaProperty.Register<SceneLayerHost, SceneRenderBudget>(nameof(RenderBudget), SceneRenderBudget.Default);

    private readonly Dictionary<SceneLayerItem, long> _insertionOrder = [];
    private readonly Dictionary<ISceneRenderable, long> _renderableInsertionOrder = [];
    private readonly HashSet<SceneEffectInstance> _effectSubscriptions = [];
    private INotifyCollectionChanged? _layersCollection;
    private INotifyCollectionChanged? _effectsCollection;
    private INotifyCollectionChanged? _renderablesCollection;
    private readonly HashSet<INotifyPropertyChanged> _renderableSubscriptions = [];
    private readonly Stopwatch _frameClock = Stopwatch.StartNew();
    private TimeSpan _lastFrame;
    private long _nextInsertionOrder;
    private bool _frameInvalidationPending;
    private SceneRenderPlan _renderPlan = SceneRenderPlan.Empty;

    static SceneLayerHost()
    {
        ItemsSourceProperty.Changed.AddClassHandler<SceneLayerHost>((host, _) => host.ResetLayers());
        EffectsSourceProperty.Changed.AddClassHandler<SceneLayerHost>((host, _) => host.ResetEffects());
        RenderablesSourceProperty.Changed.AddClassHandler<SceneLayerHost>((host, _) => host.ResetRenderables());
        RenderBudgetProperty.Changed.AddClassHandler<SceneLayerHost>((host, _) => host.InvalidateVisual());
    }

    public IEnumerable<SceneLayerItem>? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public IEnumerable<SceneEffectInstance>? EffectsSource { get => GetValue(EffectsSourceProperty); set => SetValue(EffectsSourceProperty, value); }
    public IEnumerable<ISceneRenderable>? RenderablesSource { get => GetValue(RenderablesSourceProperty); set => SetValue(RenderablesSourceProperty, value); }
    public SceneRenderBudget RenderBudget { get => GetValue(RenderBudgetProperty); set => SetValue(RenderBudgetProperty, value); }
    public SceneRenderDiagnostics RenderDiagnostics { get; } = new();
    public SceneRenderPlan RenderPlan => _renderPlan;

    /// <summary>Exports the exact scene graph used by the renderer, without GameShell UI.</summary>
    public byte[] CapturePng()
    {
        using var scene = SceneRenderPipeline.Render(_renderPlan, EffectsSource ?? [], null, Bounds.Size, CreateRenderOptions());
        using var image = SKImage.FromBitmap(scene);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        AdvanceRenderables();
        // The operation receives Avalonia's live Skia canvas. Do not encode a PNG or create an
        // Avalonia Bitmap here: animation must be a texture draw, not a per-frame image round trip.
        context.Custom(new SkiaSceneDrawOperation(Bounds, _renderPlan, EffectsSource?.ToArray() ?? [], null, CreateRenderOptions()));
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
    private void ResetRenderables()
    {
        if (_renderablesCollection is not null) _renderablesCollection.CollectionChanged -= OnRenderablesChanged;
        foreach (var renderable in _renderableSubscriptions) renderable.PropertyChanged -= OnRenderableChanged;
        _renderableSubscriptions.Clear();
        _renderableInsertionOrder.Clear();
        _renderablesCollection = RenderablesSource as INotifyCollectionChanged;
        if (_renderablesCollection is not null) _renderablesCollection.CollectionChanged += OnRenderablesChanged;
        if (RenderablesSource is not null) foreach (var renderable in RenderablesSource) AddRenderable(renderable);
        RebuildPlan();
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
        RebuildPlan();
    }
    private void OnRenderablesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset) { ResetRenderables(); return; }
        if (args.OldItems is not null) foreach (var renderable in args.OldItems.OfType<ISceneRenderable>()) RemoveRenderable(renderable);
        if (args.NewItems is not null) foreach (var renderable in args.NewItems.OfType<ISceneRenderable>()) AddRenderable(renderable);
        RebuildPlan();
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
    private void AddRenderable(ISceneRenderable renderable)
    {
        _renderableInsertionOrder.TryAdd(renderable, _nextInsertionOrder++);
        if (renderable is INotifyPropertyChanged notify && _renderableSubscriptions.Add(notify)) notify.PropertyChanged += OnRenderableChanged;
    }
    private void RemoveRenderable(ISceneRenderable renderable)
    {
        _renderableInsertionOrder.Remove(renderable);
        if (renderable is INotifyPropertyChanged notify && _renderableSubscriptions.Remove(notify)) notify.PropertyChanged -= OnRenderableChanged;
    }
    private void OnRenderableChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ISceneRenderable.Z)) RebuildPlan(); else InvalidateVisual();
    }
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
        _renderPlan = SceneRenderPlan.Create(_insertionOrder.Select(pair => new SceneRenderEntry(pair.Key, pair.Value))
            .Concat(_renderableInsertionOrder.Select(pair => new SceneRenderEntry(pair.Key, pair.Value))));
        InvalidateVisual();
    }
    private void AdvanceRenderables()
    {
        var now = _frameClock.Elapsed;
        var delta = TimeSpan.FromSeconds(Math.Clamp((now - _lastFrame).TotalSeconds, 0, .05));
        _lastFrame = now;
        var frame = new SceneFrameContext(now, delta, new SKSize((float)Bounds.Width, (float)Bounds.Height));
        if (RenderablesSource?.OfType<IFrameUpdatableSceneRenderable>().Any(renderable => renderable.Update(frame)) == true)
            RequestNextFrame();
    }

    // Avalonia forbids invalidating a visual from inside its own render pass. Frame-updated
    // renderables still need another pass, so enqueue one after the current pass completes.
    private void RequestNextFrame()
    {
        if (_frameInvalidationPending) return;
        _frameInvalidationPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _frameInvalidationPending = false;
            InvalidateVisual();
        }, DispatcherPriority.Render);
    }

    private SceneRenderOptions CreateRenderOptions() => new(RenderBudget, RenderDiagnostics);
}

/// <summary>Desktop scene presenter. Avalonia.Skia owns the destination canvas and GPU context.</summary>
internal sealed class SkiaSceneDrawOperation(
    Rect bounds,
    SceneRenderPlan plan,
    IReadOnlyList<SceneEffectInstance> effects,
    IReadOnlyList<ISceneRenderable>? renderables,
    SceneRenderOptions renderOptions) : ICustomDrawOperation
{
    private static int _missingSkiaReported;
    private static int _gpuActiveReported;
    private static int _cpuFallbackReported;
    public Rect Bounds { get; } = bounds;

    public void Render(ImmediateDrawingContext context)
    {
        var feature = context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) as ISkiaSharpApiLeaseFeature;
        if (feature is null)
        {
            if (Interlocked.Exchange(ref _missingSkiaReported, 1) == 0)
                System.Diagnostics.Trace.TraceWarning("Scene texture effects require the Avalonia Skia renderer; scene output was skipped.");
            return;
        }

        using var lease = feature.Lease();
        lease.SkCanvas.Save();
        lease.SkCanvas.ClipRect(new SKRect((float)Bounds.X, (float)Bounds.Y, (float)Bounds.Right, (float)Bounds.Bottom));
        lease.SkCanvas.Translate((float)Bounds.X, (float)Bounds.Y);
        if (SceneRenderPipeline.TryRenderGpu(lease.SkCanvas, lease.GrContext, plan, effects, renderables, Bounds.Size, renderOptions))
        {
            if (Interlocked.Exchange(ref _gpuActiveReported, 1) == 0)
                System.Diagnostics.Trace.TraceInformation("Scene pipeline is using GPU textures and shader passes.");
        }
        else
        {
            if (Interlocked.Exchange(ref _cpuFallbackReported, 1) == 0)
                System.Diagnostics.Trace.TraceWarning("Scene pipeline has no GPU texture path; using the slower CPU snapshot fallback.");
            using var scene = SceneRenderPipeline.Render(plan, effects, renderables, Bounds.Size, renderOptions);
            lease.SkCanvas.DrawBitmap(scene, 0, 0);
        }
        lease.SkCanvas.Restore();
    }

    public bool HitTest(Point point) => Bounds.Contains(point);
    public bool Equals(ICustomDrawOperation? other) => false;
    public void Dispose() { }
}
