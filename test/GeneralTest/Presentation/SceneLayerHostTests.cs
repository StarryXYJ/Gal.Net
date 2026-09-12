using System.Collections.ObjectModel;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Core.Scene;
using GalNet.Game.Controls.Scene;

namespace GeneralTest.Presentation;

public sealed class SceneLayerHostTests
{
    [Test]
    public void Missing_layer_diagnostics_are_reported_once_per_asset()
    {
        var assetId = $"missing-{Guid.NewGuid():N}";

        Assert.That(LayerImageFallback.ShouldReport(assetId), Is.True);
        Assert.That(LayerImageFallback.ShouldReport(assetId), Is.False);
    }

    [Test]
    public void Items_build_a_stable_render_plan_by_z_order()
    {
        var background = new SceneLayerItem { HandleId = "background", Z = 0, DisplayMode = LayerDisplayMode.Fill };
        var character = new SceneLayerItem { HandleId = "character", X = 700, Y = 250, Z = 20, ScaleX = 1.2, ScaleY = 1 };
        var host = new SceneLayerHost { ItemsSource = new ObservableCollection<SceneLayerItem> { background, character } };

        Assert.That(host.RenderPlan.Items.Select(entry => entry.Layer.HandleId), Is.EqualTo(["background", "character"]));
        Assert.That(host.RenderPlan.Items.Select(entry => entry.Order), Is.EqualTo([0, 20]));

        character.Z = -1;
        Assert.That(host.RenderPlan.Items.Select(entry => entry.Layer.HandleId), Is.EqualTo(["character", "background"]));
        Assert.That(host.RenderPlan.Items.Select(entry => entry.Order), Is.EqualTo([-1, 0]));
    }

    [TestCase(LayerDisplayMode.Native)]
    [TestCase(LayerDisplayMode.Tile)]
    [TestCase(LayerDisplayMode.Fill)]
    [TestCase(LayerDisplayMode.Uniform)]
    [TestCase(LayerDisplayMode.UniformToFill)]
    public void Every_display_mode_can_be_added_to_the_render_plan(LayerDisplayMode displayMode)
    {
        var item = new SceneLayerItem { HandleId = "layer", DisplayMode = displayMode, ScaleX = 2, ScaleY = 1 };
        var host = new SceneLayerHost { ItemsSource = new[] { item } };

        Assert.That(host.RenderPlan.Items, Has.Count.EqualTo(1));
        Assert.That(item.DisplayMode, Is.EqualTo(displayMode));
    }
}
