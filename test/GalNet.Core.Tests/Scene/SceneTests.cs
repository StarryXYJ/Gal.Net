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
        Assert.That(layer.Transform.X, Is.EqualTo(0));
        Assert.That(layer.Transform.Y, Is.EqualTo(0));
        Assert.That(layer.Transform.ScaleX, Is.EqualTo(1));
        Assert.That(layer.DisplayMode, Is.EqualTo(LayerDisplayMode.Native));
    }

    [Test]
    public void Layer_Should_Expose_And_Encapsulate_Animatable_Properties()
    {
        var layer = new Layer { Id = "portrait" };

        Assert.That(layer.AnimatableProperties.Select(property => property.Name),
            Is.EquivalentTo(["transform.x", "transform.y", "transform.rotationDegrees", "transform.scaleX", "transform.scaleY", "opacity", "flipbook.index"]));
        Assert.That(layer.TrySetAnimationValue("transform.x", 42, out var setError), Is.True, setError);
        Assert.That(layer.TryGetAnimationValue("transform.x", out var x), Is.True);
        Assert.That(x, Is.EqualTo(42));
        Assert.That(layer.TrySetAnimationValue("opacity", 2, out var invalidError), Is.False);
        Assert.That(invalidError, Does.Contain("does not accept"));
    }

    [Test]
    public void Layer_Flipbook_ProvidesAClampedCurrentFrame_AndCanBeAnimated()
    {
        var layer = new Layer
        {
            Id = "sprite",
            AssetId = "sheet.png",
            Flipbook = new FlipbookDefinition { Columns = 4, Rows = 4, FrameCount = 14, Index = 13.8f }
        };

        Assert.That(layer.GetCurrentFrame(), Is.EqualTo(new LayerFrame("sheet.png", 13, 4, 4)));
        Assert.That(layer.TrySetAnimationValue("flipbook.index", 99, out var error), Is.True, error);
        Assert.That(layer.GetCurrentFrame(), Is.EqualTo(new LayerFrame("sheet.png", 13, 4, 4)));
        Assert.That(layer.TryGetAnimationValue("flipbook.index", out var index), Is.True);
        Assert.That(index, Is.EqualTo(99));
    }

    [Test]
    public void Layer_StaticSource_DoesNotAcceptFlipbookIndexAnimation()
    {
        var layer = new Layer { Id = "static", AssetId = "image.png" };

        Assert.That(layer.GetCurrentFrame(), Is.EqualTo(new LayerFrame("image.png", 0, 1, 1)));
        Assert.That(layer.TrySetAnimationValue("flipbook.index", 1, out _), Is.False);
    }
}

public class GameStateTableTests
{
    [Test]
    public void RegisteredTablesAreEmptyAndUnregisteredTypesAreRejected()
    {
        var builder = new GalNet.Core.Runtime.StateCatalogBuilder();
        builder.Register<Layer>("layers", value => value.Id);
        var state = new GalNet.Core.Runtime.GameState(builder.Build());
        Assert.That(state.Table<Layer>().Values, Is.Empty);
        Assert.Throws<InvalidOperationException>(() => state.Table<EffectInstance>());
    }
    [Test]
    public void TablesRejectDuplicateIdsAndCaptureDetachedPayloads()
    {
        var builder = new GalNet.Core.Runtime.StateCatalogBuilder();
        builder.Register<Layer>("layers", value => value.Id);
        var catalog = builder.Build();
        var state = new GalNet.Core.Runtime.GameState(catalog);
        state.Table<Layer>().Add(new Layer { Id = "bg", AssetId = "before" });
        var snapshot = state.Capture();
        Assert.Throws<InvalidDataException>(() => state.Table<Layer>().Add(new Layer { Id = "bg" }));
        state.Table<Layer>().Get("bg").AssetId = "after";
        var restored = GalNet.Core.Runtime.GameState.Restore(catalog, snapshot);
        Assert.That(restored.Table<Layer>().Get("bg").AssetId, Is.EqualTo("before"));
    }
}
