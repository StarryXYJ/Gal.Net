using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Runtime.Runtime;

namespace GeneralTest.Runtime;

public class GameRuntimeSnapshotTests
{
    [Test]
    public void VariableReadResultsAreDetachedAndWritesStillUseNotificationPath()
    {
        var runtime = new GameRuntime(null);
        runtime.SetVariable("score", 42);
        var value = runtime.GetVariable("score")!;
        var snapshot = runtime.GetVariables(GalNet.Core.Variable.VariableScope.Save);
        value.SetValue(999);
        snapshot["score"].SetValue(555);
        runtime.SetVariable("later", true);
        Assert.That(runtime.GetVariable("score")!.AsInt(), Is.EqualTo(42));
        Assert.That(snapshot.ContainsKey("later"), Is.False);
        runtime.SetVariable("score", 43);
        Assert.That(runtime.GetVariable("score")!.AsInt(), Is.EqualTo(43));
        Assert.That(value.Uid, Is.EqualTo(runtime.GetVariable("score")!.Uid));
    }

    private static StateCatalog Catalog()
    {
        var builder = new StateCatalogBuilder();
        builder.Register<Layer>("layers", value => value.Id);
        builder.Register<ParticleEmitterInstance>("particles", value => value.Id);
        builder.Register<LoopAnimationState>("animations", value => value.Id);
        return builder.Build();
    }
    [Test]
    public void RestoreDetachesOldStateAndResetsEndedState()
    {
        var runtime = new GameRuntime(null, "start", stateCatalog: Catalog());
        runtime.State.Table<Layer>().Add(new Layer { Id = "layer", AssetId = "saved" });
        var snapshot = runtime.CreateSnapshot();
        var oldState = runtime.State;
        var oldInstances = runtime.SceneInstances;
        runtime.EndGame(); runtime.RestoreFrom(snapshot);
        oldState.Table<Layer>().Clear(); snapshot.States.Clear();
        Assert.Multiple(() =>
        {
            Assert.That(runtime.IsGameEnded, Is.False);
            Assert.That(runtime.State, Is.Not.SameAs(oldState));
            Assert.That(runtime.SceneInstances, Is.Not.SameAs(oldInstances));
            Assert.That(runtime.State.Table<Layer>().Get("layer").AssetId, Is.EqualTo("saved"));
        });
    }
    [Test]
    public void CaptureDoesNotShareMutableValuesAndNestedTransforms()
    {
        var catalog = Catalog();
        var runtime = new GameRuntime(null, stateCatalog: catalog);
        runtime.State.Table<Layer>().Add(new Layer { Id = "layer", AssetId = "before" });
        var snapshot = runtime.CreateSnapshot();
        runtime.State.Table<Layer>().Get("layer").Transform.X = 42;
        runtime.State.Table<Layer>().Get("layer").AssetId = "after";
        var restored = GameState.Restore(catalog, snapshot.States);
        Assert.That(restored.Table<Layer>().Get("layer").AssetId, Is.EqualTo("before"));
        Assert.That(restored.Table<Layer>().Get("layer").Transform.X, Is.Zero);
    }
    [Test]
    public void RestoredParticleIsTheCanonicalAnimationTarget()
    {
        var catalog = Catalog();
        var source = new GameRuntime(null, stateCatalog: catalog);
        var emitter = new ParticleEmitterInstance("snow", new ParticleEmitterDefinition("snowflake", EmissionRate: 72), 42);
        emitter.TrySetAnimationValue("emissionRate", 18, out _);
        source.State.Table<ParticleEmitterInstance>().Add(emitter);
        var restored = new GameRuntime(null, stateCatalog: catalog);
        restored.RestoreFrom(source.CreateSnapshot());
        Assert.That(restored.SceneInstances.TryGet<ParticleEmitterInstance>("snow", out var target), Is.True);
        Assert.That(target, Is.SameAs(restored.State.Table<ParticleEmitterInstance>().Get("snow")));
        Assert.That(target.AnimationValues["emissionRate"], Is.EqualTo(18));
        Assert.That(target.Z, Is.EqualTo(42));
    }
    [Test]
    public void LoopDefinitionIsTypedAndDetachedButExecutionControlsAreNotSaved()
    {
        var catalog = Catalog();
        var source = new GameRuntime(null, stateCatalog: catalog);
        var definition = new LoopAnimationState
        {
            Id = "loop",
            Request = new AnimationRequest
            { PlaybackHandleId = "loop", HandleId = "snow", Property = "emissionRate", From = 72, To = 18, DurationSeconds = 1.25, LoopMode = AnimationLoopMode.PingPong }
        };
        source.State.Table<LoopAnimationState>().Add(definition);
        var snapshot = source.CreateSnapshot(); definition.Request.To = 999;
        var restored = new GameRuntime(null, stateCatalog: catalog); restored.RestoreFrom(snapshot);
        Assert.That(restored.State.Table<LoopAnimationState>().Get("loop").Request!.To, Is.EqualTo(18));
        Assert.That(restored.SceneInstances.TryGet<AnimationPlaybackInstance>("loop", out _), Is.False);
    }
}
