using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Scene;
using GalNet.Core.View;
using GalNet.Game.Controls;

namespace GalNet.Avalonia.GameView.Presentation;

/// <summary>Factory implemented by one platform-specific Effect module.</summary>
public interface IAvaloniaEffectFactory
{
    EffectDefinition Definition { get; }
    string EffectId => Definition.Id;
    IAvaloniaEffect Create();
}

/// <summary>Owns one concrete visual effect and all of its stop/drain policy.</summary>
public interface IAvaloniaEffect : IDisposable
{
    Task StartAsync(EffectRequest request, IAvaloniaEffectHost host, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}

/// <summary>Small platform capability surface shared by effects; it contains no effect-specific branches.</summary>
public interface IAvaloniaEffectHost
{
    IImage? ResolveImage(string assetId);
    SceneLayerItem? FindLayer(string handleId);
    void AddOverlay(Control visual);
    void RemoveOverlay(Control visual);
    void RegisterAnimationSink(string instanceId, string propertyName, Action<double> apply, double initialValue = 0);
    void UnregisterAnimationSinks(string instanceId);
    void CompleteEffect(string instanceId);
    Task InvokeAsync(Action action);
}

/// <summary>Effect dispatcher that owns instance lifetime but delegates all actual work to Effects.</summary>
public sealed class AvaloniaEffectRuntime : IEffectView, IDisposable
{
    private readonly Dictionary<string, IAvaloniaEffectFactory> _factories;
    private readonly Dictionary<string, IAvaloniaEffect> _instances = new(StringComparer.Ordinal);
    private readonly Host _host;
    public IEffectCatalog Catalog { get; }

    public AvaloniaEffectRuntime(GamePageViewModel page, IGamePageLayerFactory layers, IEnumerable<IAvaloniaEffectFactory>? factories = null)
    {
        factories ??= DiscoverFactories();
        _factories = factories.ToDictionary(factory => factory.EffectId, StringComparer.OrdinalIgnoreCase);
        Catalog = new EffectCatalog(_factories.Values.Select(factory => factory.Definition));
        _host = new Host(page, layers, Complete);
    }

    public static IEffectCatalog CreateDefaultCatalog() => new EffectCatalog(DiscoverFactories().Select(factory => factory.Definition));
    public static IEnumerable<IAvaloniaEffectFactory> DiscoverFactories() => typeof(AvaloniaEffectRuntime).Assembly
        .GetTypes()
        .Where(type => !type.IsAbstract && typeof(IAvaloniaEffectFactory).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is not null)
        .Select(type => (IAvaloniaEffectFactory)Activator.CreateInstance(type)!);

    public async Task StartEffectAsync(EffectRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.InstanceId)) return;
        foreach (var diagnostic in Catalog.Validate(request.Id, request.TargetHandleId, request.Parameters))
            System.Diagnostics.Trace.TraceWarning("Effect diagnostic: {0}", diagnostic);
        if (!_factories.TryGetValue(request.Id, out var factory)) return;
        if (_instances.Remove(request.InstanceId, out var previous)) previous.Dispose();
        var effect = factory.Create();
        _instances.Add(request.InstanceId, effect);
        try
        {
            await effect.StartAsync(request, _host, ct);
            await _host.ApplyAnimationValuesAsync(request.InstanceId, request.AnimationValues);
        }
        catch { _instances.Remove(request.InstanceId); effect.Dispose(); throw; }
    }

    public Task StopEffectAsync(string instanceId, CancellationToken ct) =>
        _instances.TryGetValue(instanceId, out var effect) ? effect.StopAsync(ct) : Task.CompletedTask;

    public void Dispose()
    {
        foreach (var effect in _instances.Values) effect.Dispose();
        _instances.Clear();
    }

    private void Complete(string instanceId)
    {
        if (_instances.Remove(instanceId, out var effect)) effect.Dispose();
    }

    private sealed class Host(GamePageViewModel page, IGamePageLayerFactory layers, Action<string> complete) : IAvaloniaEffectHost
    {
        public IImage? ResolveImage(string assetId) => layers.ResolveLayerImage(assetId);
        public SceneLayerItem? FindLayer(string handleId) => page.Layers.FirstOrDefault(layer => layer.HandleId == handleId);
        public void AddOverlay(Control visual) => page.OverlayEffects.Add(visual);
        public void RemoveOverlay(Control visual) => page.OverlayEffects.Remove(visual);
        public void RegisterAnimationSink(string instanceId, string propertyName, Action<double> apply, double initialValue = 0) => page.RegisterEffectAnimation(instanceId, propertyName, apply, initialValue);
        public void UnregisterAnimationSinks(string instanceId) => page.UnregisterEffectAnimations(instanceId);
        public void CompleteEffect(string instanceId) => complete(instanceId);
        public Task ApplyAnimationValuesAsync(string instanceId, IReadOnlyDictionary<string, float> values) =>
            InvokeAsync(() =>
            {
                foreach (var (property, value) in values)
                    page.SetEffectAnimationValue(instanceId, property, value);
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
