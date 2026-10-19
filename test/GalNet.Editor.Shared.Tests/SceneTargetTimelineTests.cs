using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Scene;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed partial class SceneTargetAnalysisTests
{
    private static object Event(int frame, string type, object parameters, string? editorId = null) => new { frame, type, parameters, editorId = editorId ?? "event-" + frame + "-" + type };
    private static EditorEntryData Clip(object[] events, object[]? tracks = null, bool blocking = true, string loopMode = "Once") => Entry("clip", BuiltinEntryIds.PlayTimeline,
        new { timeline = new { durationFrames = 20, events, tracks = tracks ?? [], blocking, loopMode } });
    private static SceneTargetAnalysis Analyze(params EditorEntryData[] entries) => new(Graph("g"), new Dictionary<string, List<EditorEntryData>> { ["g"] = entries.ToList() });
    private static object Track(string handle, string blend = "Replace") => new { handleId = handle, property = "transform.x", blendMode = blend, keys = new[] { new { frame = 0, value = 2 }, new { frame = 20, value = 12 } } };

    [Test]
    public void TimelineQueryOrdersSameFrameEventsAndRetainsStableLocatorWithoutWriting()
    {
        object[] events = [Event(5, BuiltinEntryIds.ShowLayer, new { handleId = "created", assetId = "image", transform = new { x = 3 } }, "creator"),
            Event(5, BuiltinEntryIds.MoveLayer, new { handleId = "created", transform = new { x = 9 } }),
            Event(9, BuiltinEntryIds.HideLayer, new { handleId = "created" })];
        var clip = Clip(events); var original = JsonSerializer.Serialize(clip); var analysis = Analyze(clip);
        var future = analysis.AtTimeline("g", "clip", new(0, IncludeFuture: true)).Targets.Single();
        Assert.That(future.IsFuture, Is.True); Assert.That(future.CreatedAtFrame, Is.EqualTo(5)); Assert.That(future.Values, Is.Empty);
        Assert.That(analysis.AtTimeline("g", "clip", new(5, BeforeEventIndex: 0)).Targets, Is.Empty);
        Assert.That(analysis.AtTimeline("g", "clip", new(5, BeforeEventIndex: 1)).Targets.Single().Values["transform.x"], Is.EqualTo(3));
        var current = analysis.AtTimeline("g", "clip", new(5)).Targets.Single();
        Assert.That(current.Values["transform.x"], Is.EqualTo(9)); Assert.That(current.Presence, Is.EqualTo(TargetPresence.Definite));
        Assert.That(current.Locator, Is.EqualTo(future.Locator)); Assert.That(current.Locator.EventId, Is.EqualTo("creator"));
        Assert.That(analysis.AtTimeline("g", "clip", new(9)).Targets, Is.Empty);
        Assert.That(analysis.AtTimeline("g", "clip", new(4, BeforeEventIndex: 0)).Reachable, Is.False);
        Assert.That(JsonSerializer.Serialize(clip), Is.EqualTo(original));
        var reordered = Analyze(Clip([events[2], events[0], events[1]]));
        Assert.That(reordered.AtTimeline("g", "clip", new(5)).Targets.Single().Locator, Is.EqualTo(current.Locator));
    }

    [TestCase(true, "Once", false)]
    [TestCase(false, "Once", true)]
    [TestCase(true, "Loop", true)]
    [TestCase(true, "PingPong", true)]
    public void TimelinePropagationKeepsIntermediateObjectsPossibleUnlessBlockingOnce(bool blocking, string loop, bool possible)
    {
        var analysis = Analyze(Clip([Event(2, BuiltinEntryIds.ShowLayer, new { handleId = "transient", assetId = "image" }), Event(8, BuiltinEntryIds.HideLayer, new { handleId = "transient" })], blocking: blocking, loopMode: loop));
        var result = analysis.Before("g", null);
        Assert.That(result.Complete, Is.EqualTo(!possible)); Assert.That(result.Targets.Count, Is.EqualTo(possible ? 1 : 0));
        if (possible) { Assert.That(result.Targets.Single().Presence, Is.EqualTo(TargetPresence.Possible)); Assert.That(result.Targets.Single().Values, Is.Empty); }
    }

    [Test]
    public void TimelineSamplesReplaceAndConservativeAdditiveWithoutInventingMissingTargets()
    {
        var clip = Clip([Event(5, BuiltinEntryIds.MoveLayer, new { handleId = "layer", transform = new { x = 20 } })], [Track("layer"), Track("missing")]);
        var analysis = Analyze(Layer("source", "layer"), clip);
        Assert.That(analysis.AtTimeline("g", "clip", new(10)).Targets.Single().Values["transform.x"], Is.EqualTo(7));
        Assert.That(analysis.AtTimeline("g", "clip", new(5, BeforeEventIndex: 0)).Targets.Single().Values.ContainsKey("transform.x"), Is.False);
        var additive = Analyze(Layer("source", "layer"), Clip([], [Track("layer", "Additive")]));
        Assert.That(additive.AtTimeline("g", "clip", new(10)).Targets.Single().Values["transform.x"], Is.EqualTo(11));
        additive = Analyze(Layer("source", "layer"), Clip([Event(5, BuiltinEntryIds.MoveLayer, new { handleId = "layer", transform = new { x = 20 } })], [Track("layer", "Additive")]));
        var uncertain = additive.AtTimeline("g", "clip", new(10));
        Assert.That(uncertain.Complete, Is.False); Assert.That(uncertain.Targets.Single().Values.ContainsKey("transform.x"), Is.False);
        Assert.That(additive.Before("g", null).Targets.Single().Values["transform.x"], Is.EqualTo(32));
    }

    [Test]
    public void UnknownAndMalformedEventsStayConservativeAndFutureEventsDoNotInvalidateCurrentExistence()
    {
        var clip = Clip([Event(8, "custom.event", new { })]); var analysis = Analyze(Layer("source", "layer"), clip);
        var before = analysis.AtTimeline("g", "clip", new(0, IncludeFuture: true));
        Assert.That(before.Complete, Is.False); Assert.That(before.Targets.Single().Presence, Is.EqualTo(TargetPresence.Definite));
        var after = analysis.AtTimeline("g", "clip", new(8));
        Assert.That(after.Complete, Is.False); Assert.That(after.Targets.Single().Presence, Is.EqualTo(TargetPresence.Possible));
        clip.Parameters["timeline"] = JsonSerializer.Deserialize<JsonElement>("""{"durationFrames":20,"events":[{"frame":1,"type":"layer.show","parameters":{},"editorId":"same"},{"frame":2,"type":"layer.show","parameters":{},"editorId":"same"}]}""");
        Assert.That(Analyze(Layer("source", "layer"), clip).AtTimeline("g", "clip", new(0)).Complete, Is.False);
        clip.Parameters["timeline"] = JsonSerializer.SerializeToElement(new { durationFrames = 20, events = Enumerable.Range(0, 10001).Select(index => Event(1, BuiltinEntryIds.Wait, new { }, index.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray() });
        Assert.That(Analyze(clip).AtTimeline("g", "clip", new(0)).Complete, Is.False);
    }

    [Test]
    public void AllSupportedEventsNormalizeAndProjectTheirDeclaredSceneEffects()
    {
        object[] events = [Event(0, BuiltinEntryIds.ShowLayer, new { handleId = "layer", assetId = "image" }),
            Event(1, BuiltinEntryIds.ShowColorLayer, new { handleId = "color", color = "#FFFFFFFF" }),
            Event(2, BuiltinEntryIds.MoveLayer, new { handleId = "layer", transform = new { x = 8 } }),
            Event(3, BuiltinEntryIds.ReplaceLayer, new { handleId = "layer", assetId = "replacement" }),
            Event(4, BuiltinEntryIds.ApplyEffect, new { instanceId = "effect", program = "shader" }),
            Event(5, BuiltinEntryIds.PlayParticleEmitter, new { instanceId = "particle", parameters = new { particleTexture = "star" } }),
            Event(6, BuiltinEntryIds.BurstParticles, new { parameters = new { particleTexture = "star" } }),
            Event(7, BuiltinEntryIds.StopEffect, new { instanceId = "effect" }),
            Event(8, BuiltinEntryIds.StopParticleEmitter, new { instanceId = "particle" }),
            Event(9, BuiltinEntryIds.HideLayer, new { handleId = "color" })];
        var clip = Clip(events); var analysis = Analyze(clip);
        var definitions = clip.Parameters["timeline"].GetProperty("events").Deserialize<List<TimelineEventDefinition>>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.That(definitions.Select(item => item.Type), Is.EquivalentTo(TimelineEventCapabilities.Types));
        foreach (var item in definitions) Assert.That(TimelineEventCapabilities.NormalizeEvent(item).ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(TimelineEventCapabilities.Supports(BuiltinEntryIds.Wait), Is.False);
        Assert.Throws<InvalidDataException>(() => TimelineEventCapabilities.NormalizeEvent(new() { Type = BuiltinEntryIds.Wait }));
        var middle = analysis.AtTimeline("g", "clip", new(6)); Assert.That(middle.Complete, Is.True); Assert.That(middle.Targets, Has.Count.EqualTo(4));
        var terminal = analysis.Before("g", null); Assert.That(terminal.Complete, Is.True); Assert.That(terminal.Targets.Single().Resource, Is.EqualTo("replacement")); Assert.That(terminal.Targets.Single().Values["transform.x"], Is.EqualTo(8));
    }
}
