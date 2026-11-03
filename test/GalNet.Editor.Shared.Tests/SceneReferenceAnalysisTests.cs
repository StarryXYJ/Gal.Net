using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed partial class SceneTargetAnalysisTests
{
    [Test]
    public void RelationshipRegistryKeepsDomainsSeparateAndUnknownJsonUntouchedDuringCopy()
    {
        var layer = Layer("layer", "shared");
        var animate = Entry("animate", BuiltinEntryIds.Animate, new { handleId = "shared", playbackHandleId = "shared" });
        var stop = Entry("stop", BuiltinEntryIds.StopAnimation, new { playbackHandleId = "shared" });
        var unknown = Entry("custom", "custom.scene", new { handleId = "shared", playbackHandleId = "shared" });
        SceneAuthoringIdentities.RemapCopy([layer, animate, stop, unknown]);
        Assert.That(animate.Parameters["handleId"].GetString(), Is.EqualTo(layer.Parameters["handleId"].GetString()));
        Assert.That(stop.Parameters["playbackHandleId"].GetString(), Is.EqualTo(animate.Parameters["playbackHandleId"].GetString()).And.Not.EqualTo(layer.Parameters["handleId"].GetString()));
        Assert.That(unknown.Parameters["handleId"].GetString(), Is.EqualTo("shared"));
        Assert.That(SceneAuthoringSemantics.Find(BuiltinEntryIds.ShowLayer)!.Outputs.Single().Role, Is.EqualTo(SceneAuthoringSemantics.Find(BuiltinEntryIds.SlideTransition)!.Outputs.First().Role));
    }

    [Test]
    public void SyntacticIndexKeepsAllCreatorsAndUnreachableUsesWithoutClaimingUniqueDependency()
    {
        var graph = Graph("root", "left", "right", "merge", "unreachable"); Edge(graph, "root", "left"); Edge(graph, "root", "right"); Edge(graph, "left", "merge"); Edge(graph, "right", "merge");
        Dictionary<string, List<EditorEntryData>> groups = new()
        {
            ["root"] = [],
            ["left"] = [Layer("same", "shared")],
            ["right"] = [Layer("same", "shared")],
            ["merge"] = [Entry("use", BuiltinEntryIds.MoveLayer, new { handleId = "shared", transform = new { x = 5 } })],
            ["unreachable"] = [Entry("use", BuiltinEntryIds.HideLayer, new { handleId = "shared" }), Entry("custom", "custom.scene", new { opaque = "shared" })]
        };
        var index = new SceneReferenceIndex(groups);
        Assert.That(index.Definitions.Select(definition => definition.Owner), Is.EquivalentTo(new[] { new SceneAuthorAddress("left", "same"), new SceneAuthorAddress("right", "same") }));
        Assert.That(index.Candidates(index.References[0]), Has.Count.EqualTo(2));
        var diagnostics = SceneReferenceAnalysis.Diagnose(index, new(graph, groups));
        Assert.That(diagnostics.Any(issue => issue.Owner.GroupId == "merge"), Is.False);
        Assert.That(diagnostics.Any(issue => issue.Code == "scene.reference.unreachable"), Is.True);
        Assert.That(diagnostics.Any(issue => issue.Code == "scene.coverage.unknown"), Is.True);
        groups["left"].Clear();
        var state = SceneReferenceAnalysis.Resolve(index.References[0], new(graph, groups));
        Assert.That(state.Code, Is.EqualTo("scene.reference.possible"));
    }

    [Test]
    public void DeleteImpactUsesPositionAndHandlesCreationRemovalAndBatchConsumersAcrossGroups()
    {
        var graph = Graph("first", "next"); Edge(graph, "first", "next");
        var creator = Layer("create", "object"); var hide = Entry("hide", BuiltinEntryIds.HideLayer, new { handleId = "object" });
        var use = Entry("use", BuiltinEntryIds.MoveLayer, new { handleId = "object", transform = new { x = 4 } });
        Dictionary<string, List<EditorEntryData>> before = new() { ["first"] = [creator, hide], ["next"] = [use] };
        Dictionary<string, List<EditorEntryData>> after = new() { ["first"] = [creator], ["next"] = [use] };
        var original = JsonSerializer.Serialize(before);
        var impact = SceneReferenceAnalysis.Compare(graph, before, after);
        var change = impact.References.Single();
        Assert.That(change.Before.Code, Is.EqualTo("scene.reference.missing")); Assert.That(change.After.Code, Is.EqualTo("scene.reference.resolved"));
        Assert.That(impact.ChangedGroups, Does.Contain("next")); Assert.That(impact.Complete, Is.True);
        Assert.That(JsonSerializer.Serialize(before), Is.EqualTo(original));
        before["first"] = [creator]; after["first"] = [];
        impact = SceneReferenceAnalysis.Compare(graph, before, after);
        Assert.That(impact.References.Single().After.Code, Is.EqualTo("scene.reference.missing"));
        after["next"] = []; impact = SceneReferenceAnalysis.Compare(graph, before, after);
        Assert.That(impact.References, Is.Empty, "Consumers deleted in the same operation are not external broken references.");
    }

    [Test]
    public void TimelineIndexPreservesPathsAndPlaybackDomainAndResolvesEventsAtTheirPosition()
    {
        var graph = Graph("g");
        var timeline = Entry("timeline", BuiltinEntryIds.PlayTimeline, new
        {
            timeline = new
            {
                PlaybackHandleId = "play",
                FrameRate = 60,
                DurationFrames = 10,
                Blocking = true,
                Tracks = new[] { new { HandleId = "external", Property = "opacity", Keys = new[] { new { Frame = 0, Value = 1 } } } },
                Events = new object[] { new { EditorId = "create", Frame = 0, Type = BuiltinEntryIds.ShowLayer, Parameters = new { handleId = "inside", assetId = "image" } },
                new { EditorId = "hide", Frame = 5, Type = BuiltinEntryIds.HideLayer, Parameters = new { handleId = "inside" } } }
            }
        });
        Dictionary<string, List<EditorEntryData>> groups = new() { ["g"] = [Layer("external", "external"), timeline, Entry("stop", BuiltinEntryIds.StopAnimation, new { playbackHandleId = "play" })] };
        var index = new SceneReferenceIndex(groups);
        Assert.That(index.Complete, Is.True);
        Assert.That(index.Definitions.Any(definition => definition.Slot.Domain == SceneIdentityDomain.Playback && definition.JsonPointer == "/parameters/timeline/PlaybackHandleId"), Is.True);
        var eventUse = index.References.Single(reference => reference.EventId == "hide");
        Assert.That(eventUse.JsonPointer, Is.EqualTo("/parameters/timeline/Events/1/Parameters/handleId"));
        Assert.That(SceneReferenceAnalysis.Resolve(eventUse, new(graph, groups)).Code, Is.EqualTo("scene.reference.resolved"));
        var track = index.References.Single(reference => reference.JsonPointer.Contains("/Tracks/", StringComparison.Ordinal));
        Assert.That(SceneReferenceAnalysis.Resolve(track, new(graph, groups)).Code, Is.EqualTo("scene.reference.resolved"));
    }

    [Test]
    public void IncompleteUnknownAndConditionalStatesNeverBecomeProvenMissingTargets()
    {
        var graph = Graph("g");
        Dictionary<string, List<EditorEntryData>> groups = new() { ["g"] = [Entry("unknown", "custom.invoke", new { anything = "target" }), Entry("use", BuiltinEntryIds.HideLayer, new { handleId = "target" })] };
        var diagnostics = SceneReferenceAnalysis.Diagnose(new(groups), new(graph, groups));
        Assert.That(diagnostics.Select(issue => issue.Code), Does.Contain("scene.reference.uncertain").And.Not.Contain("scene.reference.missing"));
        groups["g"] = [Layer("conditional", "target", condition: "save.flag"), Entry("use", BuiltinEntryIds.HideLayer, new { handleId = "target" })];
        Assert.That(SceneReferenceAnalysis.Diagnose(new(groups), new(graph, groups)).Single().Code, Is.EqualTo("scene.reference.possible"));
    }

    [Test]
    public void IndexBudgetAndMalformedTimelineKeepPartialFactsExplicitlyIncomplete()
    {
        Dictionary<string, List<EditorEntryData>> groups = new() { ["g"] = Enumerable.Range(0, 6000).Select(index => Entry(index.ToString(), BuiltinEntryIds.HideLayer, new { handleId = "target" })).ToList() };
        var index = new SceneReferenceIndex(groups);
        Assert.That(index.Complete, Is.False); Assert.That(index.Coverage.Last().Code, Is.EqualTo("scene.analysis.limit"));
        Assert.That(index.References.Count, Is.LessThan(6000).And.GreaterThan(0));
        groups["g"] = [Entry("timeline", BuiltinEntryIds.PlayTimeline, new { timeline = new { Tracks = new { HandleId = "target" }, Events = new[] { new { Type = "custom.event", Parameters = new { handleId = "target" } } } } })];
        index = new(groups);
        Assert.That(index.Complete, Is.False); Assert.That(index.References, Is.Empty);
        Assert.That(index.Coverage.Select(issue => issue.Code), Does.Contain("scene.coverage.unknown").And.Contain("scene.coverage.structure"));
    }

    [Test]
    public void DeletingStopRestoresPlaybackReferenceAndCycleDeletionUsesFixedPointFacts()
    {
        var graph = Graph("g");
        var layer = Layer("layer", "layer"); var playback = Playback();
        var stop = Entry("stop", BuiltinEntryIds.StopAnimation, new { playbackHandleId = "play", mode = "CompleteImmediately" });
        var use = Entry("use", BuiltinEntryIds.StopAnimation, new { playbackHandleId = "play" });
        Dictionary<string, List<EditorEntryData>> before = new() { ["g"] = [layer, playback, stop, use] };
        Dictionary<string, List<EditorEntryData>> after = new() { ["g"] = [layer, playback, use] };
        var change = SceneReferenceAnalysis.Compare(graph, before, after).References.Single();
        Assert.That(change.Reference.Slot.Domain, Is.EqualTo(SceneIdentityDomain.Playback));
        Assert.That(change.Before.Code, Is.EqualTo("scene.reference.missing")); Assert.That(change.After.Code, Is.EqualTo("scene.reference.possible"));
        Edge(graph, "g", "g");
        var creator = Layer("created", "object"); var hide = Entry("hide", BuiltinEntryIds.HideLayer, new { handleId = "object" });
        before["g"] = [creator, hide]; after["g"] = [hide];
        var impact = SceneReferenceAnalysis.Compare(graph, before, after);
        Assert.That(impact.References.Single().After.Code, Is.EqualTo("scene.reference.missing"));
        Assert.That(impact.Complete, Is.True);
    }
}
