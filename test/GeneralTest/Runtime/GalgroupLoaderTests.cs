using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Runtime.Loader;

namespace GeneralTest.Runtime;

public class GalgroupLoaderTests
{
    [Test]
    public void LoadIntoGroup_CompilesJsonEntriesAndStructuredLayerParameters()
    {
        var group = new Group { Id = "test_group" };
        GalgroupLoader.LoadIntoGroupFromContent(group, """
            {
              "version": 1,
              "entries": [
                { "id": "entry-text", "type": "text", "parameters": { "speaker": "Alice", "content": "intro" } },
                { "id": "entry-layer", "type": "layer.show", "parameters": {
                  "handleId": "layer-handle", "assetId": "background", "z": 5, "displayMode": "Uniform",
                  "transform": { "x": 120, "y": -30, "rotationDegrees": 12, "scaleX": 2, "scaleY": 1 }
                } },
                { "id": "entry-animation", "type": "animation.play", "parameters": {
                  "plan": { "playbackHandleId": "intro-clip", "frameRate": 60, "durationFrames": 30, "blocking": true,
                    "tracks": [ { "handleId": "layer-handle", "property": "opacity", "keys": [
                      { "frame": 0, "value": 0, "interpolationToNext": "Linear" },
                      { "frame": 30, "value": 1 }
                    ] } ] }
                } }
              ]
            }
            """);

        Assert.That(group.Entries[0], Is.TypeOf<TextEntry>());
        Assert.That(group.Entries[1], Is.TypeOf<ShowLayerEntry>());
        Assert.That(group.Entries[1].Values["handleId"], Is.EqualTo("layer-handle"));
        Assert.That(group.Entries[1].Values["transform"], Does.Contain("scaleX"));
        Assert.That(group.Entries[2], Is.TypeOf<PlayAnimationPlanEntry>());
        Assert.That(group.Entries[2].Values["plan"], Does.Contain("durationFrames"));
    }

    [Test]
    public void Load_RejectsLegacyTextFormat()
    {
        var group = new Group { Id = "test" };
        Assert.That(() => GalgroupLoader.LoadIntoGroupFromContent(group, "text : content:legacy"),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("JSON"));
    }

    [Test]
    public void Load_RejectsDuplicateStableEntryIds()
    {
        var group = new Group { Id = "test" };
        Assert.That(() => GalgroupLoader.LoadIntoGroupFromContent(group, """
            { "version": 1, "entries": [
              { "id": "duplicate", "type": "text", "parameters": { "content": "a" } },
              { "id": "duplicate", "type": "text", "parameters": { "content": "b" } }
            ] }
            """), Throws.TypeOf<InvalidDataException>().With.Message.Contains("unique"));
    }
}
