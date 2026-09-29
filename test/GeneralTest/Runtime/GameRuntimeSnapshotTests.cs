using GalNet.Core.Scene;
using GalNet.Runtime.Runtime;

namespace GeneralTest.Runtime;

public class GameRuntimeSnapshotTests
{
    [Test]
    public void CreateSnapshotDoesNotShareMutableSceneState()
    {
        var runtime = new GameRuntime(null, "start");
        runtime.SceneState.Layers.Add(new Layer { Id = "layer", AssetId = "before", EffectInstanceIds = ["effect"] });

        var snapshot = runtime.CreateSnapshot();
        runtime.SceneState.Layers[0].AssetId = "after";
        runtime.SceneState.Layers[0].EffectInstanceIds.Add("another");

        Assert.That(snapshot.SceneState.Layers.Single().AssetId, Is.EqualTo("before"));
        Assert.That(snapshot.SceneState.Layers.Single().EffectInstanceIds, Is.EqualTo(new[] { "effect" }));
    }

    [Test]
    public void RestoreFromRebuildsParticleRuntimeInstanceWithSavedAnimationValues()
    {
        var source = new GameRuntime(null, "start");
        source.SceneState.ActiveParticleEmitters.Add(new ActiveParticleEmitterState
        {
            InstanceId = "snow",
            Definition = new ParticleEmitterDefinition(
                "snowflake",
                EmissionRate: 72,
                MaxParticles: 240,
                Flipbook: new ParticleFlipbookDefinition(4, 2, 7, FramesPerSecond: new ParticleFloatRange(12, 12), Loop: true)),
            Z = 42,
            AnimationValues = new Dictionary<string, float>(StringComparer.Ordinal)
            {
                ["emissionRate"] = 18,
                ["particleScale"] = 1.5f
            }
        });
        var snapshot = source.CreateSnapshot();
        var restored = new GameRuntime(null, "other");

        restored.RestoreFrom(snapshot);

        Assert.Multiple(() =>
        {
            Assert.That(restored.SceneState.ActiveParticleEmitters.Single().InstanceId, Is.EqualTo("snow"));
            Assert.That(restored.SceneInstances.TryGet<ParticleEmitterInstance>("snow", out var emitter), Is.True);
            Assert.That(emitter.Definition.ParticleTexture, Is.EqualTo("snowflake"));
            Assert.That(emitter.Definition.Flipbook, Is.EqualTo(new ParticleFlipbookDefinition(4, 2, 7, FramesPerSecond: new ParticleFloatRange(12, 12), Loop: true)));
            Assert.That(emitter.Z, Is.EqualTo(42));
            Assert.That(emitter.AnimationValues["emissionRate"], Is.EqualTo(18));
            Assert.That(emitter.AnimationValues["particleScale"], Is.EqualTo(1.5f));
        });
    }

    [Test]
    public void RestoreFromRebuildsLoopPlaybackAndPreservesReplayDefinition()
    {
        var source = new GameRuntime(null, "start");
        source.SceneState.ActiveAnimations.Add(new ActiveAnimationState
        {
            EntryType = "animation.animate",
            PlaybackHandleId = "snow-rate-loop",
            LoopMode = AnimationLoopMode.PingPong,
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["handleId"] = "snow",
                ["property"] = "emissionRate",
                ["from"] = "72",
                ["to"] = "18",
                ["duration"] = "1.25"
            }
        });
        var snapshot = source.CreateSnapshot();
        source.SceneState.ActiveAnimations[0].Parameters["to"] = "999";
        var restored = new GameRuntime(null, "other");

        restored.RestoreFrom(snapshot);

        Assert.Multiple(() =>
        {
            Assert.That(restored.SceneState.ActiveAnimations.Single().Parameters["to"], Is.EqualTo("18"));
            Assert.That(restored.SceneInstances.TryGet<AnimationPlaybackInstance>("snow-rate-loop", out var playback), Is.True);
            Assert.That(playback.LoopMode, Is.EqualTo(AnimationLoopMode.PingPong));
        });
    }
}
