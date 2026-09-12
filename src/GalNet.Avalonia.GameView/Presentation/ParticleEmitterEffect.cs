using System.Text.Json;
using GalNet.Core.Scene;
using GalNet.Core.View;

namespace GalNet.Avalonia.GameView.Presentation;

public sealed class ParticleEmitterEffectFactory : IAvaloniaEffectFactory
{
    public EffectDefinition Definition { get; } = new(
        "particle.emitter",
        EffectStage.ScenePost,
        [
            new("particleTexture", EffectParameterKind.ImageAsset), new("emissionRate", EffectParameterKind.Float, Minimum: 0),
            new("maxParticles", EffectParameterKind.Integer, Minimum: 1), new("initialVelocityX", EffectParameterKind.Float),
            new("initialVelocityY", EffectParameterKind.Float), new("noise", EffectParameterKind.Float, Minimum: 0),
            new("particleScale", EffectParameterKind.Float, Minimum: .001f), new("particleLifetime", EffectParameterKind.Float, Minimum: .01f),
            new("seed", EffectParameterKind.Integer)
        ],
        [new("emissionRate", AnimationValueKind.Float, 0), new("initialVelocityX", AnimationValueKind.Float), new("initialVelocityY", AnimationValueKind.Float), new("noise", AnimationValueKind.Float, 0), new("particleScale", AnimationValueKind.Float, .001f), new("particleLifetime", AnimationValueKind.Float, .01f)]);
    public IAvaloniaEffect Create() => new ParticleEmitterEffect();
}

public sealed class ParticleEmitterEffect : IAvaloniaEffect
{
    private ParticleEmitterControl? _emitter;
    private IAvaloniaEffectHost? _host;
    private string _instanceId = "";

    public async Task StartAsync(EffectRequest request, IAvaloniaEffectHost host, CancellationToken ct)
    {
        _host = host; _instanceId = request.InstanceId;
        await host.InvokeAsync(() =>
        {
            _emitter = new ParticleEmitterControl(host.ResolveImage(ReadTexture(request.Parameters)), request.Parameters);
            _emitter.Drained += OnDrained;
            host.AddSceneVisual(_emitter);
            host.RegisterAnimationSink(request.InstanceId, "emissionRate", value => _emitter.EmissionRate = value, _emitter.EmissionRate);
            host.RegisterAnimationSink(request.InstanceId, "initialVelocityX", value => _emitter.InitialVelocityX = value, _emitter.InitialVelocityX);
            host.RegisterAnimationSink(request.InstanceId, "initialVelocityY", value => _emitter.InitialVelocityY = value, _emitter.InitialVelocityY);
            host.RegisterAnimationSink(request.InstanceId, "noise", value => _emitter.Noise = value, _emitter.Noise);
            host.RegisterAnimationSink(request.InstanceId, "particleScale", value => _emitter.ParticleScale = value, _emitter.ParticleScale);
            host.RegisterAnimationSink(request.InstanceId, "particleLifetime", value => _emitter.ParticleLifetime = value, _emitter.ParticleLifetime);
        });
    }

    public Task StopAsync(CancellationToken ct) => _host?.InvokeAsync(() => _emitter?.StopEmission()) ?? Task.CompletedTask;
    public void Dispose()
    {
        if (_emitter is null || _host is null) return;
        _emitter.Drained -= OnDrained;
        _host.RemoveSceneVisual(_emitter);
        _emitter.Dispose();
        _emitter = null;
    }

    private void OnDrained(ParticleEmitterControl emitter)
    {
        var host = _host;
        if (host is not null) _ = host.InvokeAsync(() => host.CompleteEffect(_instanceId));
    }
    private static string ReadTexture(string parameters) { try { using var doc = JsonDocument.Parse(parameters); return doc.RootElement.TryGetProperty("particleTexture", out var value) ? value.GetString() ?? "" : ""; } catch (JsonException) { return ""; } }
}
