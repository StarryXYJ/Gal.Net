using GalNet.Core.Scene;

namespace GeneralTest.Scene;

public class LayerTests
{
    [Test]
    public void Layer_Should_Have_Default_Visible_True()
    {
        var layer = new Layer { Id = "bg", AssetId = "bg_classroom" };
        Assert.That(layer.Visible, Is.True);
        Assert.That(layer.Z, Is.EqualTo(0));
        Assert.That(layer.X, Is.EqualTo(0));
        Assert.That(layer.Y, Is.EqualTo(0));
    }
}

public class SceneStateTests
{
    [Test]
    public void SceneState_Should_Have_Empty_Collections_By_Default()
    {
        var scene = new SceneState();
        Assert.That(scene.Layers, Is.Empty);
        Assert.That(scene.ActiveControlIds, Is.Empty);
        Assert.That(scene.ActiveEffectIds, Is.Empty);
        Assert.That(scene.ActiveTransition, Is.Null);
    }

    [Test]
    public void SceneState_Should_Store_Layers_And_Controls()
    {
        var scene = new SceneState
        {
            Layers =
            {
                new Layer { Id = "bg", AssetId = "bg_classroom", Z = 0 },
                new Layer { Id = "alice", AssetId = "alice_smile", Z = 10 }
            },
            ActiveControlIds = { "default_dialogue" },
            ActiveEffectIds = { "shake" },
            ActiveTransition = "fade"
        };

        Assert.That(scene.Layers, Has.Count.EqualTo(2));
        Assert.That(scene.ActiveControlIds[0], Is.EqualTo("default_dialogue"));
        Assert.That(scene.ActiveEffectIds[0], Is.EqualTo("shake"));
        Assert.That(scene.ActiveTransition, Is.EqualTo("fade"));
    }
}
