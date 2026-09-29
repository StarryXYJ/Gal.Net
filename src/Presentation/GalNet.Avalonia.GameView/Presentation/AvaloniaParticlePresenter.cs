using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Presentation.Abstractions.View;
using GalNet.Rendering.Scene;

namespace GalNet.Avalonia.GameView.Presentation;

public sealed class AvaloniaParticlePresenter : IParticlePresenter, IDisposable
{
    private readonly GamePageViewModel _state;
    private readonly IGamePageLayerFactory _layers;
    private readonly IAvaloniaUiDispatcher _dispatcher;
    private readonly Dictionary<string, ParticleEmitter> _emitters = new(StringComparer.Ordinal);
    private long _burstSequence;

    public AvaloniaParticlePresenter(
        GamePageViewModel state,
        IGamePageLayerFactory layers,
        IAvaloniaUiDispatcher dispatcher)
    {
        _state = state;
        _layers = layers;
        _dispatcher = dispatcher;
    }

    public Task StartParticleEmitterAsync(ParticleEmitterRequest request, CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() =>
        {
            StopParticleEmitter(request.InstanceId);
            var emitter = new ParticleEmitter(
                request.InstanceId,
                _layers.ResolveTexture(request.Definition.ParticleTexture),
                request.Definition,
                request.Z);
            emitter.Drained += OnParticleDrained;
            _emitters.Add(request.InstanceId, emitter);
            _state.SceneRenderables.Add(emitter);
            RegisterAnimation(request, "emissionRate", value => emitter.EmissionRate = (float)value, emitter.EmissionRate);
            RegisterAnimation(request, "initialVelocityX", value => emitter.InitialVelocityX = (float)value, emitter.InitialVelocityX);
            RegisterAnimation(request, "initialVelocityY", value => emitter.InitialVelocityY = (float)value, emitter.InitialVelocityY);
            RegisterAnimation(request, "noise", value => emitter.Noise = (float)value, emitter.Noise);
            RegisterAnimation(request, "particleScale", value => emitter.ParticleScale = (float)value, emitter.ParticleScale);
            RegisterAnimation(request, "particleLifetime", value => emitter.ParticleLifetime = (float)value, emitter.ParticleLifetime);
            return Task.CompletedTask;
        });

    public Task BurstParticlesAsync(ParticleBurstRequest request, CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() =>
        {
            var handleId = $"__particle-burst:{Interlocked.Increment(ref _burstSequence)}";
            var emitter = new ParticleEmitter(
                handleId,
                _layers.ResolveTexture(request.Definition.ParticleTexture),
                request.Definition,
                request.Z,
                request.Count);
            emitter.Drained += OnParticleDrained;
            _emitters.Add(handleId, emitter);
            _state.SceneRenderables.Add(emitter);
            return Task.CompletedTask;
        });

    public Task StopParticleEmitterAsync(string instanceId, CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() =>
        {
            if (_emitters.TryGetValue(instanceId, out var emitter)) emitter.StopEmission();
            return Task.CompletedTask;
        });

    public void Dispose()
    {
        foreach (var id in _emitters.Keys.ToArray()) StopParticleEmitter(id);
    }

    private void RegisterAnimation(ParticleEmitterRequest request, string property, Action<double> apply, double fallback)
    {
        var initial = request.AnimationValues.TryGetValue(property, out var value) ? value : fallback;
        _state.RegisterParticleAnimation(request.InstanceId, property, apply, initial);
    }

    // Drain can occur while the frame host enumerates renderables, so removal is deferred.
    private void OnParticleDrained(ParticleEmitter emitter) =>
        _dispatcher.Dispatch(() => StopParticleEmitter(emitter.HandleId));

    private void StopParticleEmitter(string instanceId)
    {
        if (!_emitters.Remove(instanceId, out var emitter)) return;
        emitter.Drained -= OnParticleDrained;
        _state.UnregisterParticleAnimations(instanceId);
        _state.SceneRenderables.Remove(emitter);
        emitter.Dispose();
    }
}
