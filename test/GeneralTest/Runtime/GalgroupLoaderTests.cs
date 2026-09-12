using System.Text.Json;
using GalNet.Core.Compilation;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Serialization;
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
              "kind": "Compiled",
              "entries": [
                { "id": "entry-text", "type": "text", "parameters": { "speaker": "Alice", "content": "intro" } },
                { "id": "entry-layer", "type": "layer.show", "parameters": {
                  "handleId": "layer-handle", "assetId": "background", "z": 5, "displayMode": "Uniform",
                  "flipbook": { "columns": 4, "rows": 4, "frameCount": 14, "index": 2 },
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
        Assert.That(group.Entries[1].Values["flipbook"], Does.Contain("frameCount"));
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
            { "version": 1, "kind": "Compiled", "entries": [
              { "id": "duplicate", "type": "text", "parameters": { "content": "a" } },
              { "id": "duplicate", "type": "text", "parameters": { "content": "b" } }
            ] }
            """), Throws.TypeOf<InvalidDataException>().With.Message.Contains("unique"));
    }

    [Test]
    public void Load_RejectsRawDocuments()
    {
        var group = new Group { Id = "test" };
        Assert.That(() => GalgroupLoader.LoadIntoGroupFromContent(group, """
            { "version": 1, "kind": "Raw", "entries": [] }
            """), Throws.TypeOf<InvalidDataException>().With.Message.Contains("compiled"));
    }

    [TestCase(ShowLayerEntry.TypeId)]
    [TestCase(HideLayerEntry.TypeId)]
    public void Load_rejects_legacy_layer_transition_parameters(string entryType)
    {
        var parameters = new Dictionary<string, JsonElement>
        {
            ["handleId"] = JsonSerializer.SerializeToElement("layer"),
            ["transitionId"] = JsonSerializer.SerializeToElement("black")
        };
        if (entryType == ShowLayerEntry.TypeId)
            parameters["assetId"] = JsonSerializer.SerializeToElement("background.png");
        var document = new GroupDocument
        {
            Kind = GroupDocumentKind.Compiled,
            Entries = [new GroupEntryDocument { Id = "legacy-transition", Type = entryType, Parameters = parameters }]
        };
        var group = new Group { Id = "test" };

        Assert.That(() => GalgroupLoader.LoadIntoGroupFromContent(group, JsonSerializer.Serialize(document)),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("transitionId"));
    }

    [Test]
    public void Compile_ExpandsCrossFadeIntoPrimitiveAnimationPlan()
    {
        var raw = new GroupDocument
        {
            Kind = GroupDocumentKind.Raw,
            Entries =
            [
                new GroupEntryDocument
                {
                    Id = "cross-fade",
                    Type = CrossFadeTransitionEntry.TypeId,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["playbackHandleId"] = JsonSerializer.SerializeToElement("transition-playback"),
                        ["oldHandleId"] = JsonSerializer.SerializeToElement("old-background"),
                        ["newHandleId"] = JsonSerializer.SerializeToElement("new-background"),
                        ["assetId"] = JsonSerializer.SerializeToElement("new-background.png"),
                        ["transform"] = JsonSerializer.SerializeToElement(new { x = 0, y = 0, rotationDegrees = 0, scaleX = 1, scaleY = 1 })
                    }
                }
            ]
        };

        var result = GalgroupCompiler.Compile(raw);

        Assert.That(result.Document.Kind, Is.EqualTo(GroupDocumentKind.Compiled));
        Assert.That(result.Document.Entries, Has.Count.EqualTo(1));
        Assert.That(result.Document.Entries[0].Id, Is.EqualTo("cross-fade#1"));
        Assert.That(result.Document.Entries[0].Type, Is.EqualTo(PlayAnimationPlanEntry.TypeId));
        Assert.That(result.SourceMap["cross-fade"], Is.EqualTo(new[] { "cross-fade#1" }));

        var plan = result.Document.Entries[0].Parameters["plan"];
        Assert.That(plan.GetProperty("tracks").GetArrayLength(), Is.EqualTo(2));
        Assert.That(plan.GetProperty("events").GetArrayLength(), Is.EqualTo(2));
        Assert.That(plan.GetProperty("events")[0].GetProperty("type").GetString(), Is.EqualTo(ShowLayerEntry.TypeId));
        Assert.That(plan.GetProperty("events")[1].GetProperty("type").GetString(), Is.EqualTo(HideLayerEntry.TypeId));
    }

    [Test]
    public void Compile_ExpandsSlideIntoIncomingReplaceAndOutgoingAdditiveTracks()
    {
        var raw = new GroupDocument
        {
            Kind = GroupDocumentKind.Raw,
            Entries =
            [
                new GroupEntryDocument
                {
                    Id = "slide",
                    Type = SlideTransitionEntry.TypeId,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["playbackHandleId"] = JsonSerializer.SerializeToElement("slide-playback"),
                        ["fromLayerHandleId"] = JsonSerializer.SerializeToElement("old-background"),
                        ["toLayerHandleId"] = JsonSerializer.SerializeToElement("new-background"),
                        ["toAssetId"] = JsonSerializer.SerializeToElement("new-background.png"),
                        ["direction"] = JsonSerializer.SerializeToElement("Left"),
                        ["distance"] = JsonSerializer.SerializeToElement(1280f)
                    }
                }
            ]
        };

        var plan = GalgroupCompiler.Compile(raw).Document.Entries.Single().Parameters["plan"];
        var tracks = plan.GetProperty("tracks");

        Assert.That(tracks.GetArrayLength(), Is.EqualTo(2));
        Assert.That(tracks[0].GetProperty("property").GetString(), Is.EqualTo("transform.x"));
        Assert.That(tracks[0].GetProperty("blendMode").GetString(), Is.EqualTo("Additive"));
        Assert.That(tracks[0].GetProperty("keys")[1].GetProperty("value").GetSingle(), Is.EqualTo(-1280f));
        Assert.That(tracks[1].GetProperty("blendMode").GetString(), Is.EqualTo("Replace"));
        Assert.That(tracks[1].GetProperty("keys")[0].GetProperty("value").GetSingle(), Is.EqualTo(1280f));
        Assert.That(plan.GetProperty("events")[1].GetProperty("type").GetString(), Is.EqualTo(HideLayerEntry.TypeId));
    }

    [Test]
    public void Compile_ExpandsBlindsIntoAnAnimatableMaskEffect()
    {
        var raw = new GroupDocument
        {
            Kind = GroupDocumentKind.Raw,
            Entries =
            [
                new GroupEntryDocument
                {
                    Id = "blinds",
                    Type = BlindsTransitionEntry.TypeId,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["playbackHandleId"] = JsonSerializer.SerializeToElement("blinds-playback"),
                        ["oldHandleId"] = JsonSerializer.SerializeToElement("old-background"),
                        ["newHandleId"] = JsonSerializer.SerializeToElement("new-background"),
                        ["assetId"] = JsonSerializer.SerializeToElement("new-background.png"),
                        ["bladeCount"] = JsonSerializer.SerializeToElement(9),
                        ["orientation"] = JsonSerializer.SerializeToElement("Horizontal")
                    }
                }
            ]
        };

        var plan = GalgroupCompiler.Compile(raw).Document.Entries.Single().Parameters["plan"];
        var track = plan.GetProperty("tracks")[0];
        var events = plan.GetProperty("events");

        Assert.Multiple(() =>
        {
            Assert.That(track.GetProperty("handleId").GetString(), Is.EqualTo("blinds:blinds-mask"));
            Assert.That(track.GetProperty("property").GetString(), Is.EqualTo("progress"));
            Assert.That(events[0].GetProperty("type").GetString(), Is.EqualTo(ShowLayerEntry.TypeId));
            Assert.That(events[1].GetProperty("type").GetString(), Is.EqualTo(ApplyEffectEntry.TypeId));
            Assert.That(events[1].GetProperty("parameters").GetProperty("targetHandleId").GetString(), Is.EqualTo("new-background"));
            Assert.That(events[1].GetProperty("parameters").GetProperty("parameters").GetProperty("bladeCount").GetInt32(), Is.EqualTo(9));
            Assert.That(events[2].GetProperty("type").GetString(), Is.EqualTo(StopEffectEntry.TypeId));
            Assert.That(events[3].GetProperty("type").GetString(), Is.EqualTo(HideLayerEntry.TypeId));
        });
    }

    [Test]
    public void Compile_ExpandsCustomColorFadeIntoTransientOverlayPlan()
    {
        var raw = new GroupDocument
        {
            Kind = GroupDocumentKind.Raw,
            Entries =
            [
                new GroupEntryDocument
                {
                    Id = "color-fade",
                    Type = ColorFadeTransitionEntry.TypeId,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["playbackHandleId"] = JsonSerializer.SerializeToElement("color-fade-playback"),
                        ["fromLayerHandleId"] = JsonSerializer.SerializeToElement("old-background"),
                        ["toLayerHandleId"] = JsonSerializer.SerializeToElement("new-background"),
                        ["toAssetId"] = JsonSerializer.SerializeToElement("new-background.png"),
                        ["color"] = JsonSerializer.SerializeToElement("#336699"),
                        ["fadeInDuration"] = JsonSerializer.SerializeToElement(0.2f),
                        ["holdDuration"] = JsonSerializer.SerializeToElement(0.3f),
                        ["fadeOutDuration"] = JsonSerializer.SerializeToElement(0.4f)
                    }
                }
            ]
        };

        var result = GalgroupCompiler.Compile(raw);
        var plan = result.Document.Entries.Single().Parameters["plan"];
        var events = plan.GetProperty("events");

        Assert.That(events.GetArrayLength(), Is.EqualTo(4));
        Assert.That(events[0].GetProperty("type").GetString(), Is.EqualTo(ShowColorLayerEntry.TypeId));
        Assert.That(events[0].GetProperty("parameters").GetProperty("color").GetString(), Is.EqualTo("#336699"));
        Assert.That(events[1].GetProperty("type").GetString(), Is.EqualTo(HideLayerEntry.TypeId));
        Assert.That(events[2].GetProperty("type").GetString(), Is.EqualTo(ShowLayerEntry.TypeId));
        Assert.That(events[3].GetProperty("type").GetString(), Is.EqualTo(HideLayerEntry.TypeId));
        Assert.That(plan.GetProperty("frameRate").GetInt32(), Is.EqualTo(60));
        Assert.That(plan.GetProperty("durationFrames").GetInt32(), Is.EqualTo(54));
        Assert.That(events[1].GetProperty("frame").GetInt32(), Is.EqualTo(12));
    }

    [TestCase(BlackFadeTransitionEntry.TypeId, "#000000")]
    [TestCase(WhiteFadeTransitionEntry.TypeId, "#FFFFFF")]
    public void Compile_ExpandsPresetColorFadesIntoExpectedOverlay(string transitionType, string expectedColor)
    {
        var raw = new GroupDocument
        {
            Kind = GroupDocumentKind.Raw,
            Entries =
            [
                new GroupEntryDocument
                {
                    Id = "preset-fade",
                    Type = transitionType,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["playbackHandleId"] = JsonSerializer.SerializeToElement("preset-fade-playback"),
                        ["fromLayerHandleId"] = JsonSerializer.SerializeToElement("old-background"),
                        ["toLayerHandleId"] = JsonSerializer.SerializeToElement("new-background"),
                        ["toAssetId"] = JsonSerializer.SerializeToElement("new-background.png")
                    }
                }
            ]
        };

        var plan = GalgroupCompiler.Compile(raw).Document.Entries.Single().Parameters["plan"];
        Assert.That(plan.GetProperty("events")[0].GetProperty("parameters").GetProperty("color").GetString(), Is.EqualTo(expectedColor));
    }
}
