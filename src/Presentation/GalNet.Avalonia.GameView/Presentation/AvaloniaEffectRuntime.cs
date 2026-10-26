using Avalonia.Threading;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Presentation.Abstractions.View;
using GalNet.Rendering.Scene;
using GalNet.Core.Assets;
using System.Text.Json;
using System.Collections.Concurrent;

namespace GalNet.Avalonia.GameView.Presentation;

/// <summary>Owns active texture effects. The renderer owns their pixels.</summary>
public sealed class AvaloniaEffectRuntime : IDisposable, IEffectPresenter
{
    private readonly IAssetManager? _assets;
    private bool _disposed;
    private readonly ConcurrentDictionary<string, long> _preparations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ITextureEffectFactory> _textureFactories;
    private readonly Dictionary<string, ITextureEffectFactory> _programFactories = new(StringComparer.Ordinal);
    private readonly SkiaShaderEffectProgramResolver? _programs;
    private readonly Dictionary<string, SceneEffectInstance> _textureInstances = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reportedProgramFailures = new(StringComparer.Ordinal);
    private readonly GamePageViewModel _page;
    private long _nextTextureEffectInsertionOrder;
    public IEffectCatalog Catalog { get; }

    public AvaloniaEffectRuntime(GamePageViewModel page, SkiaShaderEffectProgramResolver? programs = null, IAssetManager? assets = null)
    {
        _page = page;
        _textureFactories = DiscoverTextureFactories().ToDictionary(factory => factory.Definition.Id, StringComparer.OrdinalIgnoreCase);
        _programs = programs;
        _assets = assets;
        Catalog = new EffectCatalog(_textureFactories.Values.Select(factory => factory.Definition));
    }

    public static IEffectCatalog CreateDefaultCatalog() => new EffectCatalog(DiscoverTextureFactories().Select(factory => factory.Definition));
    public static IEnumerable<ITextureEffectFactory> DiscoverTextureFactories() => typeof(SceneLayerHost).Assembly
        .GetTypes().Where(type => !type.IsAbstract && typeof(ITextureEffectFactory).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is not null)
        .Select(type => (ITextureEffectFactory)Activator.CreateInstance(type)!);

    public async Task StartEffectAsync(EffectRequest request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(request.InstanceId)) return;
        var generation = _preparations.AddOrUpdate(request.InstanceId, 1, static (_, value) => value + 1);
        if (!string.IsNullOrWhiteSpace(request.ProgramResource))
        {
            ShaderTextureBindings? textures = null;
            try
            {
                var stage = string.IsNullOrWhiteSpace(request.TargetHandleId) ? EffectStage.ScenePost : EffectStage.Layer;
                var factory = await GetProgramFactoryAsync(request.ProgramResource, stage, cancellationToken);
                textures = await LoadTexturesAsync(request, cancellationToken);
                await UiDispatcher.InvokeAsync(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_disposed || _preparations.GetValueOrDefault(request.InstanceId) != generation) return;
                    StartTextureEffect(request, factory, textures);
                    textures = null; // The published instance now owns the leases.
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                if (_reportedProgramFailures.Add(request.ProgramResource))
                    System.Diagnostics.Trace.TraceWarning("Shader program '{0}' could not be applied; using identity rendering. {1}", request.ProgramResource, error.Message);
            }
            finally { textures?.Dispose(); }
            return;
        }
        foreach (var diagnostic in Catalog.Validate(request.Id, request.TargetHandleId, request.Parameters))
            System.Diagnostics.Trace.TraceWarning("Effect diagnostic: {0}", diagnostic);
        if (_textureFactories.TryGetValue(request.Id, out var textureFactory))
        {
            await UiDispatcher.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_disposed && _preparations.GetValueOrDefault(request.InstanceId) == generation) StartTextureEffect(request, textureFactory);
            });
            return;
        }
    }

    public Task StopEffectAsync(string instanceId, CancellationToken cancellationToken)
    {
        _preparations.AddOrUpdate(instanceId, 1, static (_, value) => value + 1);
        return UiDispatcher.InvokeAsync(() => StopTextureEffect(instanceId));
    }

    public void Clear()
    {
        foreach (var id in _preparations.Keys) _preparations.AddOrUpdate(id, 1, static (_, value) => value + 1);
        foreach (var id in _textureInstances.Keys.ToArray()) StopTextureEffect(id);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();
        _programs?.Dispose();
    }

    private void StartTextureEffect(EffectRequest request, ITextureEffectFactory factory, ShaderTextureBindings? textures = null)
    {
        StopTextureEffect(request.InstanceId);
        var instance = new SceneEffectInstance(request.InstanceId, factory, request.TargetHandleId, request.Order, request.Parameters, Interlocked.Increment(ref _nextTextureEffectInsertionOrder), textures);
        _textureInstances.Add(instance.InstanceId, instance);
        _page.TextureEffects.Add(instance);
        foreach (var property in factory.Definition.AnimatableProperties)
        {
            // Keep a sink ready for a track that starts after effect.apply, but do not
            // manufacture an animated zero. Static shader parameters must remain the
            // initial value until a real animation writes this property.
            var initial = request.AnimationValues.TryGetValue(property.Name, out var value)
                ? value
                : instance.GetFloat(
                    property.Name,
                    property.DefaultValue ?? 0f,
                    property.Minimum ?? float.NegativeInfinity,
                    property.Maximum ?? float.PositiveInfinity);
            _page.RegisterEffectAnimation(instance.InstanceId, property.Name, value => instance.SetAnimatedValue(property.Name, (float)value), initial);
        }
    }

    private async Task<ShaderTextureBindings> LoadTexturesAsync(EffectRequest request, CancellationToken cancellationToken)
    {
        var bindings = new ShaderTextureBindings();
        try
        {
            if (!_programs!.TryGetDescriptor(new EffectProgramResource(request.ProgramResource), out var descriptor))
                throw new InvalidOperationException("Shader metadata is unavailable.");
            using var parameters = JsonDocument.Parse(string.IsNullOrWhiteSpace(request.Parameters) ? "{}" : request.Parameters);
            foreach (var parameter in descriptor.Parameters.Where(parameter => parameter.Kind == ShaderEffectParameterKind.Texture))
            {
                var id = parameters.RootElement.TryGetProperty(parameter.Name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                if (string.IsNullOrWhiteSpace(id))
                {
                    if (parameter.Required) throw new InvalidDataException($"Shader texture '{parameter.Name}' is required.");
                    continue;
                }
                var lease = _assets is null ? null : await _assets.AcquireAsync<SceneTexture>(id, cancellationToken);
                if (lease is null) throw new InvalidDataException($"Shader texture '{id}' could not be loaded.");
                bindings.Add(parameter.Name, lease);
            }
            return bindings;
        }
        catch { bindings.Dispose(); throw; }
    }

    private async Task<ITextureEffectFactory> GetProgramFactoryAsync(string resource, EffectStage stage, CancellationToken cancellationToken)
    {
        var key = $"{stage}:{resource}";
        if (_programFactories.TryGetValue(key, out var existing)) return existing;
        if (_programs is null) throw new InvalidOperationException("No shader project-resource source is configured for this game view.");
        var program = await _programs.ResolveAsync(new EffectProgramResource(resource), cancellationToken);
        var factory = new ShaderProgramTextureEffectFactory(resource, stage, new EffectProgramResource(resource), program);
        _programFactories.Add(key, factory);
        return factory;
    }

    private void StopTextureEffect(string instanceId)
    {
        if (!_textureInstances.Remove(instanceId, out var instance)) return;
        _page.UnregisterEffectAnimations(instanceId);
        _page.TextureEffects.Remove(instance);
        instance.Dispose();
    }

    private sealed class UiDispatcher
    {
        public static Task InvokeAsync(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess()) { action(); return Task.CompletedTask; }
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(() => { try { action(); completion.TrySetResult(); } catch (Exception error) { completion.TrySetException(error); } });
            return completion.Task;
        }
    }
}
