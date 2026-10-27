using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Runtime;
using GalNet.Core.Variable;
using GalNet.Sample.Avalonia.Debug;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GeneralTest.Diagnostics;

public sealed class SampleDebugSnapshotFactoryTests
{
    [Test]
    public void ProgressOnly_preserves_the_current_position_and_save_variables_without_scene_state()
    {
        var source = CreateSourceSnapshot(entryIndex: 7);

        var snapshot = SampleDebugSnapshotFactory.Create(source, SampleDebugSaveKind.ProgressOnly);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.NodeId, Is.EqualTo("chapter-2"));
            Assert.That(snapshot.EntryIndex, Is.EqualTo(7));
            Assert.That(snapshot.Variables["score"].AsInt(), Is.EqualTo(42));
            Assert.That(snapshot.Variables["score"], Is.Not.SameAs(source.Variables["score"]));
            Assert.That(BuiltinTestRuntime.State(snapshot).Table<Layer>().Values, Is.Empty);
            Assert.That(BuiltinTestRuntime.State(snapshot).Table<EffectInstance>().Values, Is.Empty);
            Assert.That(BuiltinTestRuntime.State(snapshot).Table<LoopAnimationState>().Values, Is.Empty);
        });
    }

    [Test]
    public void GalGroupStart_rewinds_only_the_current_group_entry_position()
    {
        var source = CreateSourceSnapshot(entryIndex: 7);

        var snapshot = SampleDebugSnapshotFactory.Create(source, SampleDebugSaveKind.GalGroupStart);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.NodeId, Is.EqualTo("chapter-2"));
            Assert.That(snapshot.EntryIndex, Is.Zero);
            Assert.That(snapshot.Variables["score"].AsInt(), Is.EqualTo(42));
            Assert.That(BuiltinTestRuntime.State(snapshot).Table<Layer>().Values, Is.Empty);
        });
    }

    private static GameSnapshot CreateSourceSnapshot(int entryIndex)
    {
        var score = new GalVariable { Name = "score" };
        score.SetValue(42);
        var runtime = BuiltinTestRuntime.Create(null, "chapter-2");
        runtime.SetEntryIndex(entryIndex);
        runtime.SetVariable("score", 42);
        runtime.State.Table<Layer>().Add(new Layer { Id = "hero", AssetId = "hero-a" });
        runtime.State.Table<EffectInstance>().Add(new EffectInstance { Id = "blur-1", EffectId = "blur" });
        runtime.State.Table<LoopAnimationState>().Add(new LoopAnimationState { Id = "fade", Request = new AnimationRequest { PlaybackHandleId = "fade" } });
        return runtime.CreateSnapshot();
    }
}
