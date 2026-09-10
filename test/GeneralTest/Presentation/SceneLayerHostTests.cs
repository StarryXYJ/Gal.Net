using System.Collections.ObjectModel;
using GalNet.Core.Scene;
using GalNet.Game.Controls;

namespace GeneralTest.Presentation;

public sealed class SceneLayerHostTests
{
    [Test]
    public void Items_are_added_and_stacked_by_z_order()
    {
        var background = new SceneLayerItem { HandleId = "background", Z = 0, DisplayMode = LayerDisplayMode.Fill };
        var character = new SceneLayerItem { HandleId = "character", X = 700, Y = 250, Z = 20, ScaleX = 1.2, ScaleY = 1 };
        var host = new SceneLayerHost { ItemsSource = new ObservableCollection<SceneLayerItem> { background, character } };

        Assert.That(host.Children.Count, Is.EqualTo(2));
        Assert.That(host.Children[0].GetValue(Avalonia.Controls.Canvas.ZIndexProperty), Is.EqualTo(0));
        Assert.That(host.Children[1].GetValue(Avalonia.Controls.Canvas.ZIndexProperty), Is.EqualTo(20));

        character.Z = -1;
        Assert.That(host.Children[0].GetValue(Avalonia.Controls.Canvas.ZIndexProperty), Is.EqualTo(-1));
        Assert.That(host.Children[1].GetValue(Avalonia.Controls.Canvas.ZIndexProperty), Is.EqualTo(0));
    }

    [TestCase(LayerDisplayMode.Native)]
    [TestCase(LayerDisplayMode.Tile)]
    [TestCase(LayerDisplayMode.Fill)]
    [TestCase(LayerDisplayMode.Uniform)]
    [TestCase(LayerDisplayMode.UniformToFill)]
    public void Every_display_mode_can_be_applied(LayerDisplayMode displayMode)
    {
        var item = new SceneLayerItem { HandleId = "layer", DisplayMode = displayMode, ScaleX = 2, ScaleY = 1 };
        var host = new SceneLayerHost { ItemsSource = new[] { item } };

        Assert.That(host.Children, Has.Count.EqualTo(1));
        Assert.That(item.DisplayMode, Is.EqualTo(displayMode));
    }
}
