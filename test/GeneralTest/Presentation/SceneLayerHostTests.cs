using System.Collections.ObjectModel;
using Avalonia.Controls;
using GalNet.Game.Controls;

namespace GeneralTest.Presentation;

public sealed class SceneLayerHostTests
{
    [Test]
    public void Items_are_positioned_and_stacked_on_the_canvas()
    {
        var background = new SceneLayerItem
        {
            Id = "background",
            Content = new Border(),
            X = 0,
            Y = 0,
            ZIndex = 0
        };
        var character = new SceneLayerItem
        {
            Id = "character",
            Content = new Border(),
            X = 700,
            Y = 250,
            ZIndex = 20
        };
        var host = new SceneLayerHost
        {
            ItemsSource = new ObservableCollection<SceneLayerItem> { background, character }
        };

        Assert.That(host.Children.Count, Is.EqualTo(2));
        Assert.That(host.Children[0], Is.SameAs(background.Content));
        Assert.That(host.Children[1], Is.SameAs(character.Content));
        Assert.That(Canvas.GetLeft(host.Children[0]), Is.EqualTo(0));
        Assert.That(Canvas.GetTop(host.Children[0]), Is.EqualTo(0));
        Assert.That(host.Children[0].GetValue(Canvas.ZIndexProperty), Is.EqualTo(0));
        Assert.That(Canvas.GetLeft(host.Children[1]), Is.EqualTo(700));
        Assert.That(Canvas.GetTop(host.Children[1]), Is.EqualTo(250));
        Assert.That(host.Children[1].GetValue(Canvas.ZIndexProperty), Is.EqualTo(20));

        character.X = 720;
        character.ZIndex = 30;

        Assert.That(Canvas.GetLeft(host.Children[1]), Is.EqualTo(720));
        Assert.That(host.Children[1].GetValue(Canvas.ZIndexProperty), Is.EqualTo(30));
    }
}
