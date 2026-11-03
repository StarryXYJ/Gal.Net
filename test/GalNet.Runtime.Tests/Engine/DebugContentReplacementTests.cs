using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Runtime.Engine;

namespace GeneralTest.Runtime;

public partial class DebugSessionTests
{
    [Test]
    public async Task NaturalGroupSequenceSurvivesSaveRestoreAndDebugSeekDoesNotAppend()
    {
        var graph = GraphOf(Entry("test.text", "first"));
        graph.Nodes.Add(new Group { Id = "second", Entries = { Entry("test.text", "second") } });
        graph.Edges.Add(new("main", 0, "second"));
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait()).BuildView();
        await using var engine = new GameEngine(graph, Runtime(), view);
        using var debug = engine.AttachDebugging(Options());

        await engine.AdvanceAsync();
        Assert.That((await debug.InspectAsync()).VisitedGroups, Is.EqualTo(new[] { "main" }));

        await debug.SeekGroupStartAsync(debug.Generation, "main");
        Assert.That((await debug.InspectAsync()).VisitedGroups, Is.EqualTo(new[] { "main" }));
        await debug.ResumeAsync(debug.Generation);
        await engine.AdvanceAsync();

        var state = await debug.InspectAsync();
        Assert.That(state.VisitedGroups, Is.EqualTo(new[] { "main", "second" }));
        Assert.That(state.RepeatedGroupVisit, Is.False);
        var save = engine.CreateSaveData();
        Assert.That(save.VisitedGroups, Is.EqualTo(new[] { "main", "second" }));

        await engine.RestoreFromAsync(save);
        Assert.That((await debug.InspectAsync()).VisitedGroups, Is.EqualTo(new[] { "main", "second" }));
    }

    [Test]
    public async Task NaturalGroupReentryIsReportedWithoutDiscardingTheSequence()
    {
        var graph = GraphOf(Entry("test.text", "line"));
        graph.Edges.Add(new("main", 0, "main"));
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait()).BuildView();
        await using var engine = new GameEngine(graph, Runtime(), view);
        using var debug = engine.AttachDebugging(Options());

        await engine.AdvanceAsync();
        await engine.AdvanceAsync();

        var state = await debug.InspectAsync();
        Assert.That(state.VisitedGroups, Is.EqualTo(new[] { "main", "main" }));
        Assert.That(state.RepeatedGroupVisit, Is.True);
        Assert.That(engine.CreateSaveData().VisitedGroups, Is.EqualTo(new[] { "main", "main" }));
    }

    [Test]
    public async Task ReplacingContentKeepsTheActiveInstanceAndDispatchesTheCandidateSuffix()
    {
        var calls = new List<string>();
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait())
            .RegisterInstruction("test.scene", context => new ImmediatePrimitiveInstance(() => calls.Add(context.Entry.StableId))).BuildView();
        await using var engine = new GameEngine(
            GraphOf(Entry("test.text", "active"), Entry("test.scene", "old"), Entry("test.text", "end")), Runtime(), view);
        using var debug = engine.AttachDebugging(Options());
        await engine.AdvanceAsync();
        var before = await debug.InspectAsync();
        var candidate = GraphOf(Entry("test.text", "active"), Entry("test.scene", "new"), Entry("test.text", "end"));

        var generation = await debug.ReplaceContentAsync(before.Generation,
            new(candidate, new(before.NodeId, before.EntryIndex, before.ActiveEntryIds), CandidateEntryIndex: 1));
        await engine.AdvanceAsync();

        Assert.Multiple(() =>
        {
            Assert.That(generation, Is.GreaterThan(before.Generation));
            Assert.That(calls, Is.EqualTo(new[] { "new" }));
            Assert.That(engine.EntryIndex, Is.EqualTo(3));
        });
        Assert.ThrowsAsync<InvalidOperationException>(() => debug.ResumeAsync(before.Generation));
    }

    [Test]
    public async Task InvalidReplacementBoundaryLeavesTheOldNavigationUsable()
    {
        var calls = new List<string>();
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait())
            .RegisterInstruction("test.scene", context => new ImmediatePrimitiveInstance(() => calls.Add(context.Entry.StableId))).BuildView();
        await using var engine = new GameEngine(
            GraphOf(Entry("test.text", "active"), Entry("test.scene", "old"), Entry("test.text", "end")), Runtime(), view);
        using var debug = engine.AttachDebugging(Options());
        await engine.AdvanceAsync();
        var before = await debug.InspectAsync();

        Assert.ThrowsAsync<InvalidOperationException>(() => debug.ReplaceContentAsync(before.Generation,
            new(GraphOf(Entry("test.text", "active"), Entry("test.scene", "new")),
                new(before.NodeId, before.EntryIndex + 1, before.ActiveEntryIds), CandidateEntryIndex: 1)));
        await engine.AdvanceAsync();

        Assert.That(calls, Is.EqualTo(new[] { "old" }));
        Assert.That(debug.Generation, Is.EqualTo(before.Generation));
    }
}
