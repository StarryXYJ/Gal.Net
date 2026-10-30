using GalNet.Core.Graph;
using GalNet.Runtime.Engine;

namespace GeneralTest.Runtime;

public partial class DebugSessionTests
{
    [Test]
    public async Task ReadingAnOlderEntranceProtectsItFromTheNextLruEviction()
    {
        var graph = GraphOf(Entry("test.text", "main"));
        foreach (var id in new[] { "second", "third", "fourth" })
            graph.Nodes.Add(new Group { Id = id, Entries = { Entry("test.text", id) } });
        graph.Edges.Add(new("main", 0, "second"));
        graph.Edges.Add(new("second", 0, "third"));
        graph.Edges.Add(new("third", 0, "fourth"));
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait()).BuildView();
        await using var engine = new GameEngine(graph, Runtime(), view);
        using var debug = engine.AttachDebugging(new() { Instructions = Options().Instructions, MaxEntranceGroups = 3 });
        await engine.AdvanceAsync();
        await engine.AdvanceAsync();
        await engine.AdvanceAsync();
        var before = await debug.InspectAsync();
        Assert.That(before.Cache!.Groups, Is.EqualTo(3));
        await debug.SeekAsync(debug.Generation, new("main", 0, []));
        Assert.That((await debug.InspectAsync()).Replay!.EntranceCacheHit, Is.True);
        await debug.SeekAsync(debug.Generation, new("third", 0, []));
        await debug.ResumeAsync(debug.Generation);
        await engine.AdvanceAsync();
        var after = await debug.InspectAsync();
        Assert.That(engine.CurrentNodeId, Is.EqualTo("fourth"));
        Assert.That(after.CachedGroups, Is.EquivalentTo(new[] { "main", "third", "fourth" }));
        Assert.That(after.Cache!.Groups, Is.EqualTo(3));
        Assert.That(after.Cache.PayloadBytes, Is.InRange(1, after.Cache.BudgetBytes));
    }

    [Test]
    public async Task ReturningNaturallyToAGroupReplacesItsEntranceAndRestoringClearsAllCacheState()
    {
        var graph = GraphOf(Entry("test.text", "main"));
        graph.Nodes.Add(new Group { Id = "second", Entries = { Entry("test.text", "second") } });
        graph.Edges.Add(new("main", 0, "second"));
        graph.Edges.Add(new("second", 0, "main"));
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait()).BuildView();
        var runtime = Runtime();
        await using var engine = new GameEngine(graph, runtime, view);
        using var debug = engine.AttachDebugging(Options());
        runtime.State.Table<DebugState>().Set(new() { Id = "scene", Value = "first" });
        await engine.AdvanceAsync();
        var firstBytes = (await debug.InspectAsync()).Cache!.PayloadBytes;
        await engine.AdvanceAsync();
        runtime.State.Table<DebugState>().Set(new() { Id = "scene", Value = "later" });
        await engine.AdvanceAsync();
        var replaced = await debug.InspectAsync();
        Assert.That(replaced.Cache!.Groups, Is.EqualTo(2));
        // The payload lengths match; only the UTF-8 group identifier lengths differ.
        Assert.That(replaced.Cache.PayloadBytes, Is.EqualTo(firstBytes * 2 + 2));
        runtime.State.Table<DebugState>().Set(new() { Id = "scene", Value = "live edit" });
        await debug.SeekAsync(debug.Generation, new("main", 0, []));
        Assert.That(runtime.State.Table<DebugState>().Get("scene").Value, Is.EqualTo("later"));
        runtime.State.Table<DebugState>().Set(new() { Id = "scene", Value = "mutated after hit" });
        await debug.ResumeAsync(debug.Generation);
        await debug.SeekAsync(debug.Generation, new("main", 0, []));
        Assert.That(runtime.State.Table<DebugState>().Get("scene").Value, Is.EqualTo("later"));
        Assert.That((await debug.InspectAsync()).Replay!.EntranceCacheHit, Is.True);
        await engine.RestoreFromAsync(engine.CreateSaveData());
        var restored = await debug.InspectAsync();
        Assert.That(restored.CachedGroups, Is.Empty);
        Assert.That(restored.Cache!.Groups, Is.Zero);
        Assert.That(restored.Cache.PayloadBytes, Is.Zero);
        Assert.That(restored.Replay, Is.Null);
    }

    [TestCase(0, 1024)]
    [TestCase(10, 0)]
    public async Task EitherZeroBudgetDisablesEntranceRetention(int groups, long bytes)
    {
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait()).BuildView();
        await using var engine = new GameEngine(GraphOf(Entry("test.text", "main")), Runtime(), view);
        using var debug = engine.AttachDebugging(new() { Instructions = Options().Instructions, MaxEntranceGroups = groups, MaxEntrancePayloadBytes = bytes });
        await engine.AdvanceAsync();
        var state = await debug.InspectAsync();
        Assert.That(state.CachedGroups, Is.Empty);
        Assert.That(state.Cache, Is.EqualTo(new GameDebugCacheStats(0, 0, bytes, groups)));
    }

    [TestCase(-1, 0)]
    [TestCase(0, 1)]
    [TestCase(1, 2)]
    public async Task PayloadBudgetHandlesExactFitAndEvictsIndependentlyOfGroupLimit(int extra, int retained)
    {
        var graph = GraphOf(Entry("test.text", "main"));
        graph.Nodes.Add(new Group { Id = "next", Entries = { Entry("test.text", "next") } });
        graph.Nodes.Add(new Group { Id = "last", Entries = { Entry("test.text", "last") } });
        graph.Edges.Add(new("main", 0, "next"));
        graph.Edges.Add(new("next", 0, "last"));
        graph.Edges.Add(new("last", 0, "next"));
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait()).BuildView();
        long entranceBytes;
        await using (var probe = new GameEngine(graph, Runtime(), view))
        {
            using var debug = probe.AttachDebugging(Options());
            await probe.AdvanceAsync();
            entranceBytes = (await debug.InspectAsync()).Cache!.PayloadBytes;
        }
        var budget = extra < 0 ? entranceBytes - 1 : entranceBytes * (extra + 1);
        await using var engine = new GameEngine(graph, Runtime(), view);
        using var session = engine.AttachDebugging(new() { Instructions = Options().Instructions, MaxEntranceGroups = 10, MaxEntrancePayloadBytes = budget });
        await engine.AdvanceAsync();
        await engine.AdvanceAsync();
        await engine.AdvanceAsync();
        var state = await session.InspectAsync();
        Assert.That(state.Cache, Is.EqualTo(new GameDebugCacheStats(retained, retained * entranceBytes, budget, 10)));
        if (retained > 0) Assert.That(state.CachedGroups, Does.Contain("last"));
        Assert.That(state.CachedGroups, Does.Not.Contain("main"));
        if (retained == 0) return;
        engine.Runtime.State.Table<DebugState>().Set(new() { Id = "scene", Value = new string('x', checked((int)budget + 1)) });
        await engine.AdvanceAsync();
        var oversized = await session.InspectAsync();
        Assert.That(oversized.CachedGroups, Is.EqualTo(new[] { "last" }), "An oversized replacement removes its old entrance without evicting other groups.");
        Assert.That(oversized.Cache!.PayloadBytes, Is.EqualTo(entranceBytes));
        await engine.AdvanceAsync();
        Assert.That((await session.InspectAsync()).Cache!.PayloadBytes, Is.Zero);
    }
}
