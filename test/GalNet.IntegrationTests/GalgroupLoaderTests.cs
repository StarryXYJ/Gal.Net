using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Compilation;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Core.Serialization;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Loader;

namespace GeneralTest.Runtime;

public class GalgroupLoaderTests
{
    private static readonly string[] CrossFadeSourceMap = ["cross-fade#1"];

    [Test]
    public void LoadIntoGroup_PreservesGenericPrimitiveArguments()
    {
        var group = new Group { Id = "test_group" };
        GalgroupLoader.LoadIntoGroupFromContent(group, """
            {
              "version": 3,
              "kind": "Compiled",
              "entries": [
                { "id": "entry-text", "typeId": "dialogue.text", "arguments": { "speaker": "Alice", "content": "intro" } },
                { "id": "entry-layer", "typeId": "layer.show", "arguments": {
                  "handleId": "layer-handle", "assetId": "background", "z": 5, "visible": true,
                  "flipbook": { "columns": 4, "rows": 4, "frameCount": 14, "index": 2 },
                  "transform": { "x": 120, "y": -30, "rotationDegrees": 12, "scaleX": 2, "scaleY": 1 }
                } },
                { "id": "entry-animation", "typeId": "timeline.play", "arguments": {
                  "timeline": { "playbackHandleId": "intro-clip", "frameRate": 60, "durationFrames": 30, "blocking": true,
                    "tracks": [ { "handleId": "layer-handle", "property": "opacity", "keys": [
                      { "frame": 0, "value": 0, "interpolationToNext": "Linear" },
                      { "frame": 30, "value": 1 }
                    ] } ] }
                } }
              ]
            }
            """);

        Assert.That(group.Entries, Has.Count.EqualTo(3));
        var text = (PrimitiveEntry)group.Entries[0];
        var layer = (PrimitiveEntry)group.Entries[1];
        var animation = (PrimitiveEntry)group.Entries[2];
        Assert.That(text.Type, Is.EqualTo("dialogue.text"));
        Assert.That(layer.Arguments.GetProperty("z").GetInt32(), Is.EqualTo(5));
        Assert.That(layer.Arguments.GetProperty("visible").GetBoolean(), Is.True);
        Assert.That(layer.Arguments.GetProperty("transform").GetProperty("scaleX").GetInt32(), Is.EqualTo(2));
        Assert.That(animation.Arguments.GetProperty("timeline").GetProperty("durationFrames").GetInt32(), Is.EqualTo(30));
    }

    [Test]
    public void CompileAndLoadPromoteBatchIdOutOfPrimitiveArguments()
    {
        var parameters = new DynamicParameterTable(
        [
            new DynamicParameterDescriptor("value", typeof(int), isRequired: true),
            new DynamicParameterDescriptor("batchId", typeof(string))
        ]);
        var catalog = new TargetProfileEntryCatalog(
            [new TestEntryModule("test", [new DefaultPrimitiveEntryBase("test.run", parameters)])]);
        var source = new GroupDocument
        {
            Entries =
            {
                new GroupEntryDocument
                {
                    Id = "run-1",
                    Type = "test.run",
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["value"] = JsonSerializer.SerializeToElement(7),
                        ["batchId"] = JsonSerializer.SerializeToElement("opening")
                    }
                }
            }
        };

        var compiled = GalgroupCompiler.Compile(source, catalog).Document;

        Assert.Multiple(() =>
        {
            Assert.That(compiled.Entries[0].BatchId, Is.EqualTo("opening"));
            Assert.That(compiled.Entries[0].Arguments.TryGetProperty("batchId", out _), Is.False);
            Assert.That(compiled.Entries[0].Arguments.GetProperty("value").GetInt32(), Is.EqualTo(7));
        });

        var group = new Group { Id = "main" };
        GalgroupLoader.LoadIntoGroupFromContent(group, JsonSerializer.Serialize(compiled));
        Assert.That(((PrimitiveEntry)group.Entries.Single()).BatchId, Is.EqualTo("opening"));
    }

    [Test]
    public void Load_RejectsLegacyTextFormat()
    {
        var group = new Group { Id = "test" };
        Assert.That(() => GalgroupLoader.LoadIntoGroupFromContent(group, "text : content:legacy"),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("primitive-envelope"));
    }

    [Test]
    public void Load_RejectsDuplicateStableEntryIds()
    {
        var group = new Group { Id = "test" };
        Assert.That(() => GalgroupLoader.LoadIntoGroupFromContent(group, """
            { "version": 3, "kind": "Compiled", "entries": [
              { "id": "duplicate", "typeId": "dialogue.text", "arguments": { "content": "a" } },
              { "id": "duplicate", "typeId": "dialogue.text", "arguments": { "content": "b" } }
            ] }
            """), Throws.TypeOf<InvalidDataException>().With.Message.Contains("unique"));
    }

    [Test]
    public void Load_RejectsRawDocuments()
    {
        var group = new Group { Id = "test" };
        Assert.That(() => GalgroupLoader.LoadIntoGroupFromContent(group, """
            { "version": 2, "kind": "Raw", "entries": [] }
            """), Throws.TypeOf<InvalidDataException>().With.Message.Contains("compiled"));
    }

    [Test]
    public static void Load_RejectsPreviousFormatVersion()
    {
        var group = new Group { Id = "test" };

        Assert.That(() => GalgroupLoader.LoadIntoGroupFromContent(group, """
            { "version": 2, "kind": "Compiled", "entries": [] }
            """), Throws.TypeOf<InvalidDataException>().With.Message.Contains("version"));
    }

    [Test]
    public void Compile_ExpandsCrossFadeIntoPrimitiveTimeline()
    {
        var raw = new GroupDocument
        {
            Kind = GroupDocumentKind.Raw,
            Entries =
            [
                new GroupEntryDocument
                {
                    Id = "cross-fade",
                    Type = BuiltinEntryIds.CrossFadeTransition,
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

        var result = GalgroupCompiler.Compile(raw, new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules()));

        Assert.That(result.Document.Kind, Is.EqualTo(GroupDocumentKind.Compiled));
        Assert.That(result.Document.Entries, Has.Count.EqualTo(1));
        Assert.That(result.Document.Entries[0].Id, Is.EqualTo("cross-fade#1"));
        Assert.That(result.Document.Entries[0].TypeId, Is.EqualTo(BuiltinEntryIds.PlayTimeline));
        Assert.That(result.SourceMap["cross-fade"], Is.EqualTo(CrossFadeSourceMap));

        var timeline = result.Document.Entries[0].Arguments.GetProperty("timeline");
        Assert.That(timeline.GetProperty("blocking").GetBoolean(), Is.True);
        Assert.That(timeline.GetProperty("skippable").GetBoolean(), Is.True);
        Assert.That(timeline.GetProperty("tracks").GetArrayLength(), Is.EqualTo(2));
        Assert.That(timeline.GetProperty("events").GetArrayLength(), Is.EqualTo(2));
        Assert.That(timeline.GetProperty("events")[0].GetProperty("type").GetString(), Is.EqualTo(BuiltinEntryIds.ShowLayer));
        Assert.That(timeline.GetProperty("events")[1].GetProperty("type").GetString(), Is.EqualTo(BuiltinEntryIds.HideLayer));
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
                    Type = BuiltinEntryIds.SlideTransition,
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

        var timeline = GalgroupCompiler.Compile(raw, new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules())).Document.Entries.Single().Arguments.GetProperty("timeline");
        var tracks = timeline.GetProperty("tracks");

        Assert.That(timeline.GetProperty("blocking").GetBoolean(), Is.True);
        Assert.That(timeline.GetProperty("skippable").GetBoolean(), Is.True);
        Assert.That(tracks.GetArrayLength(), Is.EqualTo(2));
        Assert.That(tracks[0].GetProperty("property").GetString(), Is.EqualTo("transform.x"));
        Assert.That(tracks[0].GetProperty("blendMode").GetString(), Is.EqualTo("Additive"));
        Assert.That(tracks[0].GetProperty("keys")[1].GetProperty("value").GetSingle(), Is.EqualTo(-1280f));
        Assert.That(tracks[1].GetProperty("blendMode").GetString(), Is.EqualTo("Replace"));
        Assert.That(tracks[1].GetProperty("keys")[0].GetProperty("value").GetSingle(), Is.EqualTo(1280f));
        Assert.That(timeline.GetProperty("events")[1].GetProperty("type").GetString(), Is.EqualTo(BuiltinEntryIds.HideLayer));
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
                    Type = BuiltinEntryIds.BlindsTransition,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["playbackHandleId"] = JsonSerializer.SerializeToElement("blinds-playback"),
                        ["oldHandleId"] = JsonSerializer.SerializeToElement("old-background"),
                        ["newHandleId"] = JsonSerializer.SerializeToElement("new-background"),
                        ["assetId"] = JsonSerializer.SerializeToElement("new-background.png"),
                        ["maskProgram"] = JsonSerializer.SerializeToElement("a1000000000000000000000000000008"),
                        ["bladeCount"] = JsonSerializer.SerializeToElement(9),
                        ["orientation"] = JsonSerializer.SerializeToElement("Horizontal")
                    }
                }
            ]
        };

        var timeline = GalgroupCompiler.Compile(raw, new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules())).Document.Entries.Single().Arguments.GetProperty("timeline");
        var track = timeline.GetProperty("tracks")[0];
        var events = timeline.GetProperty("events");

        Assert.Multiple(() =>
        {
            Assert.That(timeline.GetProperty("blocking").GetBoolean(), Is.True);
            Assert.That(timeline.GetProperty("skippable").GetBoolean(), Is.True);
            Assert.That(track.GetProperty("handleId").GetString(), Is.EqualTo("blinds:blinds-mask"));
            Assert.That(track.GetProperty("property").GetString(), Is.EqualTo("progress"));
            Assert.That(events[0].GetProperty("type").GetString(), Is.EqualTo(BuiltinEntryIds.ApplyEffect));
            Assert.That(events[0].GetProperty("parameters").GetProperty("program").GetString(), Is.EqualTo("a1000000000000000000000000000008"));
            Assert.That(events[0].GetProperty("parameters").GetProperty("targetHandleId").GetString(), Is.EqualTo("new-background"));
            Assert.That(events[0].GetProperty("parameters").GetProperty("parameters").GetProperty("bladeCount").GetInt32(), Is.EqualTo(9));
            Assert.That(events[1].GetProperty("type").GetString(), Is.EqualTo(BuiltinEntryIds.ShowLayer));
            Assert.That(events[2].GetProperty("type").GetString(), Is.EqualTo(BuiltinEntryIds.StopEffect));
            Assert.That(events[3].GetProperty("type").GetString(), Is.EqualTo(BuiltinEntryIds.HideLayer));
        });
    }

    [Test]
    public void Compile_ExpandsCustomColorFadeIntoTransientOverlayTimeline()
    {
        var raw = new GroupDocument
        {
            Kind = GroupDocumentKind.Raw,
            Entries =
            [
                new GroupEntryDocument
                {
                    Id = "color-fade",
                    Type = BuiltinEntryIds.ColorFadeTransition,
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

        var result = GalgroupCompiler.Compile(raw, new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules()));
        var timeline = result.Document.Entries.Single().Arguments.GetProperty("timeline");
        var events = timeline.GetProperty("events");

        Assert.That(timeline.GetProperty("blocking").GetBoolean(), Is.True);
        Assert.That(timeline.GetProperty("skippable").GetBoolean(), Is.True);
        Assert.That(events.GetArrayLength(), Is.EqualTo(4));
        Assert.That(events[0].GetProperty("type").GetString(), Is.EqualTo(BuiltinEntryIds.ShowColorLayer));
        Assert.That(events[0].GetProperty("parameters").GetProperty("color").GetString(), Is.EqualTo("#336699"));
        Assert.That(events[1].GetProperty("type").GetString(), Is.EqualTo(BuiltinEntryIds.HideLayer));
        Assert.That(events[2].GetProperty("type").GetString(), Is.EqualTo(BuiltinEntryIds.ShowLayer));
        Assert.That(events[3].GetProperty("type").GetString(), Is.EqualTo(BuiltinEntryIds.HideLayer));
        Assert.That(timeline.GetProperty("frameRate").GetInt32(), Is.EqualTo(60));
        Assert.That(timeline.GetProperty("durationFrames").GetInt32(), Is.EqualTo(54));
        Assert.That(events[1].GetProperty("frame").GetInt32(), Is.EqualTo(12));
    }

    [TestCase(BuiltinEntryIds.BlackFadeTransition, "#000000")]
    [TestCase(BuiltinEntryIds.WhiteFadeTransition, "#FFFFFF")]
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

        var timeline = GalgroupCompiler.Compile(raw, new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules())).Document.Entries.Single().Arguments.GetProperty("timeline");
        Assert.That(timeline.GetProperty("events")[0].GetProperty("parameters").GetProperty("color").GetString(), Is.EqualTo(expectedColor));
    }
}

file sealed class TestEntryModule(string id, IEnumerable<PrimitiveEntryBase> entries) : EntryModuleBase(id, entries);
