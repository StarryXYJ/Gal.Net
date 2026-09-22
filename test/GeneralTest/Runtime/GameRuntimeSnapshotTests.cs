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
}
