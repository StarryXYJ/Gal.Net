using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Rendering.Scene;
using GalNet.Core.Scene;
using GalNet.Core.View;

namespace GalNet.Avalonia.GameView.Presentation;

/// <summary>Legacy overlay effect extension point. ParticleEmitter remains here until its GPU migration.</summary>
public interface IAvaloniaEffectFactory
{
    EffectDefinition Definition { get; }
    string EffectId => Definition.Id;
    IAvaloniaEffect Create();
}

public interface IAvaloniaEffect : IDisposable
{
    Task StartAsync(EffectRequest request, IAvaloniaEffectHost host, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}

public interface IAvaloniaEffectHost
{
    IImage? ResolveImage(string assetId);
    SceneLayerItem? FindLayer(string handleId);
    void AddSceneVisual(Control visual);
    void RemoveSceneVisual(Control visual);
    void RegisterAnimationSink(string instanceId, string propertyName, Action<double> apply, double initialValue = 0);
    void UnregisterAnimationSinks(string instanceId);
    void CompleteEffect(string instanceId);
    Task InvokeAsync(Action action);
}

/// <summary>Owns active effects. Texture effects mutate generic instance data; the renderer owns their pixels.</summary>
public sealed class AvaloniaEffectRuntime : IEffectView, IDisposable
{
    private readonly Dictionary<string, ITextureEffectFactory> _textureFactories;
    private readonly Dictionary<string, IAvaloniaEffectFactory> _legacyFactories;
    private readonly Dictionary<string, SceneEffectInstance> _textureInstances = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IAvaloniaEffect> _legacyInstances = new(StringComparer.Ordinal);
    private readonly GamePageViewModel _page;
    private readonly Host _host;
    private long _nextTextureEffectInsertionOrder;
    public IEffectCatalog Catalog { get; }

    public AvaloniaEffectRuntime(GamePageViewModel page, IGamePageLayerFactory layers, IEnumerable<IAvaloniaEffectFactory>? factories = null)
    {
        _page = page;
        _textureFactories = DiscoverTextureFactories().ToDictionary(factory => factory.Definition.Id, StringComparer.OrdinalIgnoreCase);
        _legacyFactories = (factories ?? DiscoverLegacyFactories()).ToDictionary(factory => factory.EffectId, StringComparer.OrdinalIgnoreCase);
        Catalog = new EffectCatalog(_textureFactories.Values.Select(factory => factory.Definition).Concat(_legacyFactories.Values.Select(factory => factory.Definition)));
        _host = new Host(page, layers, CompleteLegacy);
    }

    public static IEffectCatalog CreateDefaultCatalog() => new EffectCatalog(DiscoverTextureFactories().Select(factory => factory.Definition).Concat(DiscoverLegacyFactories().Select(factory => factory.Definition)));
    public static IEnumerable<IAvaloniaEffectFactory> DiscoverFactories() => DiscoverLegacyFactories();
    public static IEnumerable<ITextureEffectFactory> DiscoverTextureFactories() => typeof(SceneLayerHost).Assembly
        .GetTypes().Where(type => !type.IsAbstract && typeof(ITextureEffectFactory).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is not null)
        .Select(type => (ITextureEffectFactory)Activator.CreateInstance(type)!);
    private static IEnumerable<IAvaloniaEffectFactory> DiscoverLegacyFactories() => typeof(AvaloniaEffectRuntime).Assembly
        .GetTypes().Where(type => !type.IsAbstract && typeof(IAvaloniaEffectFactory).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is not null)
        .Select(type => (IAvaloniaEffectFactory)Activator.CreateInstance(type)!);

    public async Task StartEffectAsync(EffectRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.InstanceId)) return;
        foreach (var diagnostic in Catalog.Validate(request.Id, request.TargetHandleId, request.Parameters))
            System.Diagnostics.Trace.TraceWarning("Effect diagnostic: {0}", diagnostic);
        if (_textureFactories.TryGetValue(request.Id, out var textureFactory))
        {
            await _host.InvokeAsync(() => StartTextureEffect(request, textureFactory));
            return;
        }
        if (!_legacyFactories.TryGetValue(request.Id, out var legacyFactory)) return;
        if (_legacyInstances.Remove(request.InstanceId, out var previous)) previous.Dispose();
        var effect = legacyFactory.Create();
        _legacyInstances.Add(request.InstanceId, effect);
        try
        {
            await effect.StartAsync(request, _host, ct);
            await _host.ApplyAnimationValuesAsync(request.InstanceId, request.AnimationValues);
        }
        catch { _legacyInstances.Remove(request.InstanceId); effect.Dispose(); throw; }
    }

    public Task StopEffectAsync(string instanceId, CancellationToken ct)
    {
        if (_textureInstances.ContainsKey(instanceId)) return _host.InvokeAsync(() => StopTextureEffect(instanceId));
        return _legacyInstances.TryGetValue(instanceId, out var effect) ? effect.StopAsync(ct) : Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var id in _textureInstances.Keys.ToArray()) StopTextureEffect(id);
        foreach (var effect in _legacyInstances.Values) effect.Dispose();
        _legacyInstances.Clear();
    }

    private void StartTextureEffect(EffectRequest request, ITextureEffectFactory factory)
    {
        StopTextureEffect(request.InstanceId);
        var instance = new SceneEffectInstance(request.InstanceId, factory, request.TargetHandleId, request.Order, request.Parameters, Interlocked.Increment(ref _nextTextureEffectInsertionOrder));
        _textureInstances.Add(instance.InstanceId, instance);
        _page.TextureEffects.Add(instance);
        foreach (var property in factory.Definition.AnimatableProperties)
        {
            var initial = request.AnimationValues.TryGetValue(property.Name, out var value) ? value : 0f;
            _page.RegisterEffectAnimation(instance.InstanceId, property.Name, value => instance.SetAnimatedValue(property.Name, (float)value), initial);
        }
    }

    private void StopTextureEffect(string instanceId)
    {
        if (!_textureInstances.Remove(instanceId, out var instance)) return;
        _page.UnregisterEffectAnimations(instanceId);
        _page.TextureEffects.Remove(instance);
    }

    private void CompleteLegacy(string instanceId)
    {
        if (_legacyInstances.Remove(instanceId, out var effect)) effect.Dispose();
    }

    private sealed class Host(GamePageViewModel page, IGamePageLayerFactory layers, Action<string> complete) : IAvaloniaEffectHost
    {
        public IImage? ResolveImage(string assetId) => layers.ResolveTexture(assetId).AvaloniaImage;
        public SceneLayerItem? FindLayer(string handleId) => page.Layers.FirstOrDefault(layer => layer.HandleId == handleId);
        public void AddSceneVisual(Control visual) => page.SceneVisuals.Add(visual);
        public void RemoveSceneVisual(Control visual) => page.SceneVisuals.Remove(visual);
        public void RegisterAnimationSink(string instanceId, string propertyName, Action<double> apply, double initialValue = 0) => page.RegisterEffectAnimation(instanceId, propertyName, apply, initialValue);
        public void UnregisterAnimationSinks(string instanceId) => page.UnregisterEffectAnimations(instanceId);
        public void CompleteEffect(string instanceId) => complete(instanceId);
        public Task ApplyAnimationValuesAsync(string instanceId, IReadOnlyDictionary<string, float> values) => InvokeAsync(() =>
        {
            foreach (var (property, value) in values) page.SetEffectAnimationValue(instanceId, property, value);
        });
        public Task InvokeAsync(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess()) { action(); return Task.CompletedTask; }
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(() => { try { action(); completion.TrySetResult(); } catch (Exception error) { completion.TrySetException(error); } });
            return completion.Task;
        }
    }
}
