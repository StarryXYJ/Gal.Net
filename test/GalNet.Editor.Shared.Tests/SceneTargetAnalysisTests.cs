using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed class SceneTargetAnalysisTests
{
    private static EditorEntryData Entry(string id, string type, object parameters, string condition = "") => new()
    { StableId = id, Type = type, Condition = condition, Parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(parameters))! };
    private static EditorEntryData Layer(string id, string handle, float x = 4, string condition = "") => Entry(id, BuiltinEntryIds.ShowLayer, new { handleId = handle, assetId = "same-image", transform = new { x } }, condition);
    private static EditorGraphDocument Graph(params string[] ids) => new() { RootNodeId = ids[0], Nodes = ids.Select(id => new EditorGraphNodeDto { Id = id, Name = id, Type = "Group" }).ToList() };
    private static void Edge(EditorGraphDocument graph, string from, string to) => graph.Edges.Add(new() { FromNodeId = from, ToNodeId = to });

    [Test]
    public void QueryIsBeforeEntryPureAndKeepsSameResourceInstancesDistinct()
    {
        var graph = Graph("a");
        Dictionary<string, List<EditorEntryData>> groups = new() { ["a"] = [Layer("left", "opaque1"), Layer("right", "opaque2", 8), Entry("end", BuiltinEntryIds.Wait, new { })] };
        var before = JsonSerializer.Serialize(groups); var analysis = new SceneTargetAnalysis(graph, groups);
        Assert.That(analysis.Before("a", "left").Targets, Is.Empty);
        var result = analysis.Before("a", "end"); Assert.That(result.Complete, Is.True); Assert.That(result.Targets, Has.Count.EqualTo(2));
        Assert.That(result.Targets.Select(target => target.Presence), Is.All.EqualTo(TargetPresence.Definite));
        Assert.That(result.Targets.Select(target => target.Locator.EntryId), Is.EquivalentTo(new[] { "left", "right" }));
        Assert.That(result.Targets[0].Values["transform.x"], Is.EqualTo(4)); Assert.That(result.Targets[0].Properties.Any(property => property.Name == "flipbook.index"), Is.False);
        Assert.That(JsonSerializer.Serialize(groups), Is.EqualTo(before)); Assert.That(analysis.Before("a", "removed").Reachable, Is.False);
    }
    [Test]
    public void BranchMergeIntersectsPresenceAndSeparatesUniqueValuesFromExistence()
    {
        var graph = Graph("root", "left", "right", "merge", "unreachable"); Edge(graph, "root", "left"); Edge(graph, "root", "right"); Edge(graph, "left", "merge"); Edge(graph, "right", "merge");
        Dictionary<string, List<EditorEntryData>> groups = new()
        {
            ["root"] = [Layer("base", "common")],
            ["left"] = [Layer("a", "left-only"), Entry("move", BuiltinEntryIds.MoveLayer, new { handleId = "common", transform = new { x = 90 } })],
            ["right"] = [],
            ["merge"] = [],
            ["unreachable"] = [Layer("hidden", "never")]
        };
        var analysis = new SceneTargetAnalysis(graph, groups); var result = analysis.Before("merge", null);
        var common = result.Targets.Single(target => target.Handle == "common"); Assert.That(common.Presence, Is.EqualTo(TargetPresence.Definite)); Assert.That(common.Values.ContainsKey("transform.x"), Is.False);
        Assert.That(result.Targets.Single(target => target.Handle == "left-only").Presence, Is.EqualTo(TargetPresence.Possible)); Assert.That(analysis.Before("unreachable", null).Reachable, Is.False);
    }
    [Test]
    public void DifferentObjectKindsSharingAnIdAcrossBranchesNeverAdvertiseDefiniteTypedCapabilities()
    {
        var graph = Graph("root", "left", "right", "merge"); Edge(graph, "root", "left"); Edge(graph, "root", "right"); Edge(graph, "left", "merge"); Edge(graph, "right", "merge");
        Dictionary<string, List<EditorEntryData>> groups = new() { ["root"] = [], ["left"] = [Layer("layer", "shared")], ["right"] = [Entry("effect", BuiltinEntryIds.ApplyEffect, new { instanceId = "shared" })], ["merge"] = [] };
        var target = new SceneTargetAnalysis(graph, groups).Before("merge", null).Targets.Single();
        Assert.That(target.Presence, Is.EqualTo(TargetPresence.Possible)); Assert.That(target.Kind, Is.EqualTo(SceneTargetKind.Unresolved)); Assert.That(target.Properties, Is.Empty); Assert.That(target.Values, Is.Empty);
    }
    [Test]
    public void ConditionsDeletesReplacementsAndUnknownEntriesRemainConservative()
    {
        var graph = Graph("g"); Dictionary<string, List<EditorEntryData>> groups = new() { ["g"] = [Layer("a", "a"), Layer("b", "b", condition: "save.flag"), Layer("skip", "skip", condition: "false"), Entry("unknown", "custom.scene", new { }), Layer("certain", "c"), Entry("hide", BuiltinEntryIds.HideLayer, new { handleId = "a" }), Entry("replace", BuiltinEntryIds.ReplaceLayer, new { handleId = "c", assetId = "new-image" }), Entry("end", BuiltinEntryIds.Wait, new { })] };
        var analysis = new SceneTargetAnalysis(graph, groups); var result = analysis.Before("g", "end"); Assert.That(result.Complete, Is.False);
        Assert.That(result.Targets.Select(target => target.Handle), Is.EquivalentTo(new[] { "b", "c" }));
        Assert.That(result.Targets.Single(target => target.Handle == "b").Presence, Is.EqualTo(TargetPresence.Possible));
        Assert.That(result.Targets.Single(target => target.Handle == "c").Presence, Is.EqualTo(TargetPresence.Definite)); Assert.That(result.Targets.Single(target => target.Handle == "c").Resource, Is.EqualTo("new-image"));
    }
    [Test]
    public void CyclesConvergeAndBudgetNeverPublishesDefiniteFacts()
    {
        var graph = Graph("start", "loop"); Edge(graph, "start", "loop"); Edge(graph, "loop", "loop");
        Dictionary<string, List<EditorEntryData>> groups = new() { ["start"] = [], ["loop"] = [Layer("created", "loop-instance")] };
        var result = new SceneTargetAnalysis(graph, groups).Before("loop", "created"); Assert.That(result.Complete, Is.True); Assert.That(result.Targets.Single().Presence, Is.EqualTo(TargetPresence.Possible));
        result = new SceneTargetAnalysis(graph, groups, 1).Before("start", null); Assert.That(result.Complete, Is.False); Assert.That(result.Targets.All(target => target.Presence == TargetPresence.Possible), Is.True);
    }
    [Test]
    public void AnimationInvalidatesValuesAndVideoCannotBeCalledDefinite()
    {
        var graph = Graph("g"); Dictionary<string, List<EditorEntryData>> groups = new() { ["g"] = [Layer("layer", "layer"), Entry("animate", BuiltinEntryIds.Animate, new { handleId = "layer" }), Entry("video", BuiltinEntryIds.PlayVideo, new { handleId = "video", asset = "movie" }), Entry("end", BuiltinEntryIds.Wait, new { })] };
        var result = new SceneTargetAnalysis(graph, groups).Before("g", "end"); Assert.That(result.Targets.Single(target => target.Handle == "layer").Values, Is.Empty); Assert.That(result.Targets.Single(target => target.Handle == "video").Presence, Is.EqualTo(TargetPresence.Possible));
    }
    [Test]
    public void InvalidAuthorDataAndUnimplementedTimelineAreIncompleteWithoutThrowing()
    {
        var graph = Graph("g"); Dictionary<string, List<EditorEntryData>> groups = new() { ["g"] = [Layer("layer", "layer"), Entry("bad", BuiltinEntryIds.MoveLayer, new { handleId = "layer", transform = new { x = "oops" } }), Entry("timeline", BuiltinEntryIds.PlayTimeline, new { timeline = new { } })] };
        var result = new SceneTargetAnalysis(graph, groups).Before("g", null); Assert.That(result.Complete, Is.False); Assert.That(result.Targets.Single().Presence, Is.EqualTo(TargetPresence.Possible));
    }
    [Test]
    public void ParticlesAndEffectsUseDeclaredCapabilitiesAndTypedRemoval()
    {
        var graph = Graph("g"); Dictionary<string, List<EditorEntryData>> groups = new() { ["g"] = [Entry("particle", BuiltinEntryIds.PlayParticleEmitter, new { instanceId = "p", parameters = new { particleTexture = "star" } }), Entry("effect", BuiltinEntryIds.ApplyEffect, new { instanceId = "e", program = "shader" }), Entry("end", BuiltinEntryIds.Wait, new { }), Entry("stop", BuiltinEntryIds.StopParticleEmitter, new { instanceId = "p" })] };
        var analysis = new SceneTargetAnalysis(graph, groups); var result = analysis.Before("g", "end"); Assert.That(result.Targets, Has.Count.EqualTo(2)); Assert.That(result.Targets.Single(target => target.Handle == "p").Properties.Any(property => property.Name == "emissionRate"), Is.True);
        Assert.That(result.Targets.Single(target => target.Handle == "e").Properties.Select(property => property.Name), Is.EqualTo(new[] { "progress" })); Assert.That(analysis.Before("g", null).Targets.Single().Handle, Is.EqualTo("e"));
    }
    [Test]
    public void CopyRemapsOnlyDeclaredInternalSlotsIncludingTimelineTracksAndEvents()
    {
        var source = Layer("layer", "internal");
        var timeline = Entry("timeline", BuiltinEntryIds.PlayTimeline, new { timeline = new { PlaybackHandleId = "playback", Tracks = new[] { new { HandleId = "internal", Property = "opacity", note = "internal" }, new { HandleId = "external", Property = "opacity", note = "external" } }, Events = new[] { new { Type = BuiltinEntryIds.ShowLayer, Parameters = new { handleId = "event-object", assetId = "image" } }, new { Type = BuiltinEntryIds.HideLayer, Parameters = new { handleId = "event-object", assetId = "unrelated" } } } }, future = "internal" });
        var stop = Entry("stop", BuiltinEntryIds.StopAnimation, new { playbackHandleId = "playback" });
        SceneAuthoringIdentities.RemapCopy([source, timeline, stop]);
        var handle = source.Parameters["handleId"].GetString(); Assert.That(handle, Is.Not.EqualTo("internal")); var root = timeline.Parameters["timeline"];
        Assert.That(root.GetProperty("Tracks")[0].GetProperty("HandleId").GetString(), Is.EqualTo(handle)); Assert.That(root.GetProperty("Tracks")[0].GetProperty("note").GetString(), Is.EqualTo("internal")); Assert.That(root.GetProperty("Tracks")[1].GetProperty("HandleId").GetString(), Is.EqualTo("external"));
        Assert.That(root.GetProperty("Events")[0].GetProperty("Parameters").GetProperty("handleId").GetString(), Is.EqualTo(root.GetProperty("Events")[1].GetProperty("Parameters").GetProperty("handleId").GetString()).And.Not.EqualTo("event-object"));
        Assert.That(stop.Parameters["playbackHandleId"].GetString(), Is.EqualTo(root.GetProperty("PlaybackHandleId").GetString()).And.Not.EqualTo("playback")); Assert.That(timeline.Parameters["future"].GetString(), Is.EqualTo("internal"));
    }
}
