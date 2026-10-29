using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed partial class EffectParameterLocalizationTests
{
    private static JsonElement UserTimeline() => JsonSerializer.Deserialize<JsonElement>("""
        {
          "playbackHandleId":"25f2011d83274f93a67024b0028f3e4d", "frameRate":60, "durationFrames":60,
          "tracks":[{"handleId":"c2121d4d69544d4d8a584d392dfedbe6","property":"radius","blendMode":"Replace",
            "keys":[{"frame":0,"value":32,"interpolationToNext":"Linear"},
                    {"frame":10,"value":34.3,"inTangent":0,"outTangent":0,"interpolationToNext":"Linear"}]}],
          "events":[{"frame":8,"type":"layer.show","editorId":"dc1ce9a65c694a589651cc1f16d3411d",
            "parameters":{"transform":{},"z":0,"opacity":1,"displayMode":"Native","handleId":"fcef9d991e18436e96e96f97be479092"}}]
        }
        """);

    [Test]
    public void UserTimelineTraversesTracksKeysAndEventContractWithoutInventingReferencesOrMutatingJson()
    {
        var entry = Entry("clip", BuiltinEntryIds.PlayTimeline, new { timeline = UserTimeline() });
        var document = Document(entry); var before = JsonSerializer.Serialize(document);
        var index = Scan(document);
        Assert.That(index.Issues, Is.Empty);
        Assert.That(index.Complete, Is.True);
        Assert.That(index.References, Is.Empty);
        Assert.That(JsonSerializer.Serialize(document), Is.EqualTo(before));
    }

    [Test]
    public void TimelineAuthorMetadataAndCaseInsensitiveWireNamesRemainCoveredWhileUnknownMembersAreDiagnosed()
    {
        var entry = Entry("clip", BuiltinEntryIds.PlayTimeline, new
        {
            timeline = new
            {
                FrameRate = 60,
                DurationFrames = 30,
                LoopMode = "Loop",
                Tracks = new[] { new { HandleId = "fx", Property = "radius", BlendMode = "Additive", Keys = new[] {
                new { Frame = 0, Value = 0, InterpolationToNext = "CubicHermite", EditorTangents = new { Incoming = "Auto", Outgoing = "Free", Linked = true } } } } },
                Events = new[] { new { Frame = 0, Execution = "Once", Type = BuiltinEntryIds.ShowLayer, EditorId = "event", Parameters = new { handleId = "layer", transform = new { X = 12 } } } }
            }
        });
        Assert.That(Scan(Document(entry)).Issues, Is.Empty);
        entry.Parameters["timeline"] = JsonSerializer.Deserialize<JsonElement>("""{"tracks":[{"handleId":"fx","keys":[{"frame":0,"value":"bad","extension":{"x":1}}]}]}""");
        var issues = Scan(Document(entry)).Issues;
        Assert.That(issues.Select(issue => (issue.Code, issue.JsonPointer)), Is.EquivalentTo(new[] {
            ("value.type", "/timeline/tracks/0/keys/0/value"), ("field.unknown", "/timeline/tracks/0/keys/0/extension") }));
    }

    [Test]
    public void TimelineEffectEventsDiscoverTheirProgramAndReuseItsParameterSchema()
    {
        var shader = new ShaderEffectDescriptor(new("nested"), 1, "source", new HashSet<EffectStage> { EffectStage.ScenePost },
            [new("radius", "radius", ShaderEffectParameterKind.Float)]);
        var entry = Entry("clip", BuiltinEntryIds.PlayTimeline, new
        {
            timeline = new
            {
                tracks = Array.Empty<object>(),
                events = new[] {
            new { frame = 0, type = BuiltinEntryIds.ApplyEffect, parameters = new { program = "nested", instanceId = "fx", parameters = new { radius = 32, angle = 0, unused = new { text = "literal" } } } } }
            }
        });
        var document = Document(entry);
        Assert.That(EntryParameterMetadata.ProgramIds(document), Is.EqualTo(new[] { "nested" }));
        Assert.That(Scan(document, new(Programs: new Dictionary<string, ShaderEffectDescriptor?> { ["nested"] = shader })).Issues, Is.Empty);
        var issue = Scan(document).Issues.Single();
        Assert.That(issue.Code, Is.EqualTo("field.opaque"));
        Assert.That(issue.JsonPointer, Is.EqualTo("/timeline/events/0/parameters/parameters"));
    }

    [Test]
    public void TimelineEventPayloadUsesTheProjectCatalogAndPreservesItsLocalizationSemantics()
    {
        var payload = new FieldDescriptor("caption", new(typeof(string)), semantic: FieldValueSemantic.LocalizedText);
        var catalog = new TargetProfileEntryCatalog([new DefaultEntryModule("custom", [
            DefaultPrimitiveEntryBase.ForDto<TimelineOptions>(BuiltinEntryIds.PlayTimeline),
            new DefaultPrimitiveEntryBase(BuiltinEntryIds.ApplyEffect, new DynamicParameterTable([new(payload)]))])]);
        var document = Document(Entry("clip", BuiltinEntryIds.PlayTimeline, new
        {
            timeline = new
            {
                events = new[] {
            new { frame = 0, type = BuiltinEntryIds.ApplyEffect, parameters = new { caption = "line" } } }
            }
        }));
        var index = new LocalizationReferenceIndex(document, catalog, [new("I18n/zh-CN/test.json", """{"line":"正文"}""")]);
        Assert.That(index.References.Single().JsonPointer, Is.EqualTo("/timeline/events/0/parameters/caption"));
        Assert.That(index.Issues, Is.Empty);
    }
}
