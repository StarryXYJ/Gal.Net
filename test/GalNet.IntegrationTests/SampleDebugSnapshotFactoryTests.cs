using GalNet.Core.Runtime;
using GalNet.Core.Scene;
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
            Assert.That(snapshot.SceneState.Layers, Is.Empty);
            Assert.That(snapshot.SceneState.ActiveEffects, Is.Empty);
            Assert.That(snapshot.SceneState.ActiveAnimations, Is.Empty);
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
            Assert.That(snapshot.SceneState.Layers, Is.Empty);
        });
    }

    private static GameSnapshot CreateSourceSnapshot(int entryIndex)
    {
        var score = new GalVariable { Name = "score" };
        score.SetValue(42);
        return new GameSnapshot
        {
            NodeId = "chapter-2",
            EntryIndex = entryIndex,
            Variables = new Dictionary<string, GalVariable> { ["score"] = score },
            SceneState = new SceneState
            {
                Layers = [new Layer { Id = "hero", AssetId = "hero-a" }],
                ActiveEffects = [new ActiveEffectState { Id = "blur", InstanceId = "blur-1" }],
                ActiveAnimations = [new ActiveAnimationState { PlaybackHandleId = "fade" }]
            }
        };
    }
}
