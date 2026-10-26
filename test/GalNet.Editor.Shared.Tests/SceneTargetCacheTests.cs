using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed partial class SceneTargetAnalysisTests
{
    [Test]
    public void ReusingAnAnalysisWithNewResourceDeclarationsCannotReuseOldCapabilities()
    {
        Dictionary<string, List<EditorEntryData>> groups = new() { ["g"] = [Entry("fx", BuiltinEntryIds.ApplyEffect, new { instanceId = "fx", program = "program" })] };
        var previous = new SceneTargetAnalysis(Graph("g"), groups, programs: new Dictionary<string, GalNet.Contracts.Builtins.Scene.ShaderEffectDescriptor?> { ["program"] = EffectDescriptor() });
        Assert.That(previous.Before("g", null).Complete, Is.True);
        var next = new SceneTargetAnalysis(Graph("g"), groups, programs: new Dictionary<string, GalNet.Contracts.Builtins.Scene.ShaderEffectDescriptor?> { ["program"] = null }, previousAnalysis: previous);
        Assert.That(next.Before("g", null).Complete, Is.False);
        Assert.That(next.Before("g", null).Targets.Single().Properties.Select(property => property.Name), Is.EqualTo(new[] { "progress" }));
        Assert.That(next.ReusedGroupCount, Is.Zero);
    }

    [Test]
    public void PrefixCheckpointsAreBoundedReuseUnchangedGroupsAndMatchFreshAnalysis()
    {
        var graph = Graph("root", "a", "b", "merge"); Edge(graph, "root", "a"); Edge(graph, "root", "b"); Edge(graph, "a", "merge"); Edge(graph, "b", "merge");
        Dictionary<string, List<EditorEntryData>> groups = new()
        {
            ["root"] = [Layer("origin", "layer")],
            ["a"] = Enumerable.Range(0, 2000).Select(index => Entry("a" + index, BuiltinEntryIds.Wait, new { })).ToList(),
            ["b"] = Enumerable.Range(0, 2000).Select(index => Entry("b" + index, BuiltinEntryIds.Wait, new { })).ToList(),
            ["merge"] = []
        };
        var original = new SceneTargetAnalysis(graph, groups);
        var transferred = original.EntryTransferCount;
        original.Before("a", "a1999"); var firstRead = original.EntryTransferCount;
        for (var index = 0; index < 100; index++) original.Before("a", "a1999");
        Assert.That(original.EntryTransferCount, Is.EqualTo(firstRead));
        Assert.That(firstRead - transferred, Is.LessThan(64));
        var edited = JsonSerializer.Deserialize<List<EditorEntryData>>(JsonSerializer.Serialize(groups["a"]))!;
        edited[1990].Condition = "false";
        var next = groups.ToDictionary(pair => pair.Key, pair => pair.Value); next["a"] = edited;
        var incremental = new SceneTargetAnalysis(graph, next, previousAnalysis: original);
        var fresh = new SceneTargetAnalysis(graph, next);
        Assert.That(incremental.ReusedGroupCount, Is.GreaterThanOrEqualTo(3));
        Assert.That(incremental.EntryTransferCount, Is.LessThan(64));
        foreach (var group in groups.Keys)
            Assert.That(SceneTargetAnalysis.Equivalent(incremental.Before(group, null), fresh.Before(group, null)), Is.True);
        for (var index = 0; index < 2000; index++) incremental.Before("a", "a" + index);
        Assert.That(incremental.CheckpointCount, Is.LessThanOrEqualTo(128));
        Edge(graph, "merge", "a");
        incremental = new(graph, next, previousAnalysis: incremental); fresh = new(graph, next);
        Assert.That(SceneTargetAnalysis.Equivalent(incremental.Before("a", null), fresh.Before("a", null)), Is.True);
        var exhausted = new SceneTargetAnalysis(graph, next, 1, previousAnalysis: incremental);
        Assert.That(exhausted.Before("a", null).Complete, Is.False);
    }
}
