using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Runtime;
using GalNet.Sample.Avalonia.Services;

namespace GalNet.IntegrationTests;

public sealed class PersistentSceneRestorerTests
{
    [Test]
    public async Task RestoreAsync_replays_effects_and_particles_through_the_runner_presenters()
    {
        var runtime = new GameRuntime(null);
        runtime.SceneState.ActiveEffects.Add(new ActiveEffectState
        {
            Id = "color-grade",
            InstanceId = "grade",
            ProgramResource = "grade-program",
            AnimationValues = new Dictionary<string, float>(StringComparer.Ordinal)
            {
                ["brightness"] = 0.5f
            }
        });
        runtime.SceneState.ActiveParticleEmitters.Add(new ActiveParticleEmitterState
        {
            InstanceId = "snow",
            Definition = new ParticleEmitterDefinition("snowflake", EmissionRate: 24),
            Z = 12
        });
        var effects = new RecordingEffectPresenter();
        var particles = new RecordingParticlePresenter();
        var restorer = new PersistentSceneRestorer(
            effects,
            particles,
            new NoopAnimationPresenter(),
            new NoopLayerPresenter());

        await restorer.RestoreAsync(runtime, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(effects.Started.Single().InstanceId, Is.EqualTo("grade"));
            Assert.That(effects.Started.Single().AnimationValues["brightness"], Is.EqualTo(0.5f));
            Assert.That(particles.Started.Single().InstanceId, Is.EqualTo("snow"));
            Assert.That(particles.Started.Single().Z, Is.EqualTo(12));
        });
    }

    private sealed class RecordingEffectPresenter : IEffectPresenter
    {
        public List<EffectRequest> Started { get; } = [];
        public Task StartEffectAsync(EffectRequest request, CancellationToken cancellationToken)
        {
            Started.Add(request);
            return Task.CompletedTask;
        }

        public Task StopEffectAsync(string instanceId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingParticlePresenter : IParticlePresenter
    {
        public List<ParticleEmitterRequest> Started { get; } = [];
        public Task StartParticleEmitterAsync(ParticleEmitterRequest request, CancellationToken cancellationToken)
        {
            Started.Add(request);
            return Task.CompletedTask;
        }

        public Task BurstParticlesAsync(ParticleBurstRequest request, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task StopParticleEmitterAsync(string instanceId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class NoopAnimationPresenter : IAnimationPresenter
    {
        public Task<AnimationOutcome> AnimateAsync(AnimationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(AnimationOutcome.Completed);

        public Task<AnimationPlanPlayResult> PlayAnimationPlanAsync(
            AnimationPlanDefinition plan,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AnimationPlanPlayResult { Outcome = AnimationOutcome.Completed });

        public bool CompleteAnimationImmediately(string playbackHandleId) => false;
    }

    private sealed class NoopLayerPresenter : ILayerPresenter
    {
        public void ShowLayer(LayerRenderRequest request) { }
        public void ReplaceLayer(string handleId, string assetId) { }
        public void HideLayer(string handleId) { }
        public void MoveLayer(string handleId, LayerTransform transform, float z, float durationSeconds) { }
    }
}
