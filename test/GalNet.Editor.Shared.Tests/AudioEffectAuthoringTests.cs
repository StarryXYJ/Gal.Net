using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed class AudioEffectAuthoringTests
{
    private static EditorEntryData Entry(string id, string type, object parameters, string condition = "") => new()
    { StableId = id, Type = type, Condition = condition, Parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(parameters))! };
    private static EditorEntryData Apply(string id, string instance = "effect", string target = "same", string scope = "track", string type = "seffect.reverb", string condition = "")
        => Entry(id, BuiltinEntryIds.ApplyAudioSEffect, new { scope, targetId = target, instanceId = instance, id = type, parameters = SEffectCatalog.Builtins.TryGet(type, out var definition) ? definition.DefaultParameters : JsonSerializer.SerializeToElement(new { }) }, condition);
    private static EditorGraphDocument Graph(params string[] ids) => new() { RootNodeId = ids[0], Nodes = ids.Select(id => new EditorGraphNodeDto { Id = id, Type = "Group" }).ToList() };
    private static void Edge(EditorGraphDocument graph, string from, string to) => graph.Edges.Add(new() { FromNodeId = from, ToNodeId = to });

    [Test]
    public void AttachmentIdentitySeparatesScopesAndStoppingSourcesKeepsEffects()
    {
        var entries = new List<EditorEntryData> { Apply("track"), Apply("bus", scope: "bus"), Apply("second", instance: "second"),
            Entry("source", BuiltinEntryIds.StopAudio, new { trackId = "same" }),
            Entry("stop", BuiltinEntryIds.StopAudioSEffect, new { targetId = "same", instanceId = "effect" }),
            Entry("all", BuiltinEntryIds.StopAudioSEffect, new { targetId = "same", all = true }) };
        var analysis = new SceneTargetAnalysis(Graph("g"), new Dictionary<string, List<EditorEntryData>> { ["g"] = entries });
        Assert.That(analysis.Before("g", "track").AudioEffects, Is.Empty);
        Assert.That(analysis.Before("g", "stop").AudioEffects, Has.Count.EqualTo(3));
        Assert.That(analysis.Before("g", "all").AudioEffects, Has.Count.EqualTo(2));
        var final = analysis.Before("g", null);
        Assert.That(final.AudioEffects.Single().Key.Scope, Is.EqualTo(SEffectScope.Bus));
        Assert.That(final.Complete && final.AudioComplete, Is.True);
    }

    [Test]
    public void BranchMergeKeepsEverySourceAndPossibleAlgorithmWithoutInventingAUniqueType()
    {
        var graph = Graph("root", "left", "right", "merge"); Edge(graph, "root", "left"); Edge(graph, "root", "right"); Edge(graph, "left", "merge"); Edge(graph, "right", "merge");
        Dictionary<string, List<EditorEntryData>> groups = new()
        {
            ["root"] = [],
            ["left"] = [Apply("left")],
            ["right"] = [Apply("right", type: "seffect.gain"), Apply("conditional", instance: "optional", condition: "flag")],
            ["merge"] = []
        };
        var result = new SceneTargetAnalysis(graph, groups).Before("merge", null);
        var common = result.AudioEffects.Single(effect => effect.Key.InstanceId == "effect");
        Assert.That(common.Presence, Is.EqualTo(TargetPresence.Definite));
        Assert.That(common.Sources.Select(source => source.EntryId), Is.EquivalentTo(new[] { "left", "right" }));
        Assert.That(common.Types, Is.EquivalentTo(new[] { "seffect.reverb", "seffect.gain" }));
        Assert.That(result.AudioEffects.Single(effect => effect.Key.InstanceId == "optional").Presence, Is.EqualTo(TargetPresence.Possible));
    }

    [Test]
    public void UnknownSemanticsRemainsIncompleteAfterStopAllButKnownMetadataErrorsCanBeRemoved()
    {
        Dictionary<string, List<EditorEntryData>> groups = new()
        {
            ["g"] = [Apply("bad", type: "custom.unknown"),
            Entry("stop", BuiltinEntryIds.StopAudioSEffect, new { targetId = "same", all = true })]
        };
        var analysis = new SceneTargetAnalysis(Graph("g"), groups);
        Assert.That(analysis.Before("g", "stop").AudioComplete, Is.False);
        Assert.That(analysis.Before("g", null).AudioComplete, Is.True);
        groups["g"].Insert(1, Entry("unknown", "custom.invoke", new { }));
        analysis = new(Graph("g"), groups);
        Assert.That(analysis.Before("g", null).AudioComplete, Is.False);
    }

    [Test]
    public void NewAndCopiedIdentitiesAreQualifiedAndOnlyDeclaredInternalReferencesAreRemapped()
    {
        var first = SceneAuthoringIdentities.NewEntry(BuiltinEntryIds.ApplyAudioSEffect, []);
        var second = SceneAuthoringIdentities.NewEntry(BuiltinEntryIds.ApplyAudioSEffect, []);
        Assert.That(first["instanceId"].GetString(), Is.Not.EqualTo(second["instanceId"].GetString()));
        var create = Apply("create"); var stop = Entry("stop", BuiltinEntryIds.StopAudioSEffect, new { targetId = "same", instanceId = "effect" });
        var bus = Entry("bus", BuiltinEntryIds.StopAudioSEffect, new { scope = "bus", targetId = "same", instanceId = "effect" });
        var other = Entry("other", BuiltinEntryIds.StopAudioSEffect, new { targetId = "other", instanceId = "effect" });
        var all = Entry("all", BuiltinEntryIds.StopAudioSEffect, new { targetId = "same", all = true });
        SceneAuthoringIdentities.RemapCopy([create, stop, bus, other, all]);
        Assert.That(stop.Parameters["instanceId"].GetString(), Is.EqualTo(create.Parameters["instanceId"].GetString()).And.Not.EqualTo("effect"));
        Assert.That(bus.Parameters["instanceId"].GetString(), Is.EqualTo("effect"));
        Assert.That(other.Parameters["instanceId"].GetString(), Is.EqualTo("effect"));
        Assert.That(create.Parameters["id"].GetString(), Is.EqualTo("seffect.reverb"));
        Assert.That(all.Parameters.ContainsKey("instanceId"), Is.False);
    }

    [Test]
    public void ReferenceIndexUsesAudioDomainAndAttachmentAndReportsMissingAfterTargetChanges()
    {
        var stop = Entry("stop", BuiltinEntryIds.StopAudioSEffect, new { scope = "bus", targetId = "same", instanceId = "effect" });
        Dictionary<string, List<EditorEntryData>> groups = new() { ["g"] = [Apply("track"), Apply("bus", scope: "bus"), stop] };
        var index = new SceneReferenceIndex(groups); var reference = index.References.Single();
        Assert.That(index.Candidates(reference).Single().Owner.EntryId, Is.EqualTo("bus"));
        Assert.That(SceneReferenceAnalysis.Resolve(reference, new(Graph("g"), groups)).Code, Is.EqualTo("scene.reference.resolved"));
        groups["g"].RemoveAt(1);
        Assert.That(SceneReferenceAnalysis.Resolve(reference, new(Graph("g"), groups)).Code, Is.EqualTo("scene.reference.missing"));
    }

    [Test]
    public void UpdateDoesNotCreateInstancesAndInvalidAudioDoesNotInvalidateVisualFacts()
    {
        Dictionary<string, List<EditorEntryData>> groups = new()
        {
            ["g"] = [Apply("effect"),
            Entry("update", BuiltinEntryIds.UpdateAudioSEffect, new { targetId = "same", instanceId = "missing", parameters = new { mix = .3 } }),
            Entry("invalid", BuiltinEntryIds.ApplyAudioSEffect, new { targetId = "same", scope = "invalid" }), Apply("future", instance: "future")]
        };
        var analysis = new SceneTargetAnalysis(Graph("g"), groups);
        Assert.That(analysis.Before("g", "invalid").AudioEffects, Has.Count.EqualTo(1));
        var beforeFuture = analysis.Before("g", "future");
        Assert.That(beforeFuture.AudioEffects.Single().Key.InstanceId, Is.EqualTo("effect"));
        Assert.That(beforeFuture.Complete, Is.True); Assert.That(beforeFuture.AudioComplete, Is.False);
    }

    [Test]
    public void LoopAndBudgetAreFiniteAndNeverAdvertiseAudioAsVisualObjects()
    {
        var graph = Graph("g", "unreachable"); Edge(graph, "g", "g");
        Dictionary<string, List<EditorEntryData>> groups = new() { ["g"] = [Apply("create", condition: "flag"), Entry("wait", BuiltinEntryIds.Wait, new { })], ["unreachable"] = [Apply("unused")] };
        var result = new SceneTargetAnalysis(graph, groups).Before("g", "wait");
        Assert.That(result.AudioEffects.Single().Presence, Is.EqualTo(TargetPresence.Possible)); Assert.That(result.Targets, Is.Empty);
        var limited = new SceneTargetAnalysis(graph, groups, budget: 1).Before("g", "wait");
        Assert.That(limited.AudioComplete, Is.False); Assert.That(limited.AudioEffects.SelectMany(effect => effect.Sources).Any(source => source.EntryId == "unused"), Is.False);
    }
}
