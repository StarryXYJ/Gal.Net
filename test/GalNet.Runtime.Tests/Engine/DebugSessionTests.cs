using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Core.Variable;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Runtime;

namespace GeneralTest.Runtime;

public class DebugSessionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task StopOrExternalRestoreCancelsAnInFlightSeekAndDrainsOldWork(bool restore)
    {
        var blocker = new ReplayBlocker();
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait())
            .RegisterInstruction("test.scene", _ => blocker).BuildView();
        await using var engine = new GameEngine(GraphOf(Entry("test.text", "first"), Entry("test.scene", "blocker")), Runtime(), view);
        using var debug = engine.AttachDebugging(Options());
        var initial = engine.Runtime.CreateSnapshot();
        await engine.AdvanceAsync();
        var operation = debug.SeekAsync(debug.Generation, new("main", 2, []));
        await blocker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (restore) engine.RestoreFrom(initial);
        else await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.ThrowsAsync<OperationCanceledException>(async () => await operation.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.That(engine.State, Is.EqualTo(restore ? GameEngineState.Ready : GameEngineState.Stopped));
        if (restore)
        {
            Assert.That((await debug.InspectAsync()).CachedGroups, Is.Empty);
            Assert.That(engine.EntryIndex, Is.Zero);
        }
    }
    [Test]
    public async Task SceneRewindRemovesLaterObjectsPreservesVariablesAndDoesNotOverwriteEntrance()
    {
        var graph = GraphOf(Entry("test.text", "first"), Entry("test.scene", "object"), Entry("test.text", "last"));
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait())
            .RegisterInstruction("test.scene", context => new ImmediatePrimitiveInstance(() =>
                context.Runtime.State.Table<Layer>().Set(new Layer { Id = "late", AssetId = "after" }))).BuildView();
        await using var engine = new GameEngine(graph, Runtime(), view);
        using var debug = engine.AttachDebugging(Options());
        engine.Runtime.SetVariable("save.counter", 7);
        await engine.AdvanceAsync();
        await engine.AdvanceAsync();
        Assert.That(engine.Runtime.State.Table<Layer>().TryGet("late", out _), Is.True);
        await debug.SeekAsync(debug.Generation, new("main", 0, []));
        Assert.That(engine.Runtime.State.Table<Layer>().TryGet("late", out _), Is.False);
        Assert.That(engine.Runtime.GetVariable("counter")!.AsInt(), Is.EqualTo(7));
        var generation = debug.Generation;
        await debug.SeekAsync(generation, new("main", 2, []));
        Assert.That(engine.Runtime.State.Table<Layer>().TryGet("late", out _), Is.True);
        await debug.SeekAsync(debug.Generation, new("main", 0, []));
        Assert.That(engine.Runtime.State.Table<Layer>().TryGet("late", out _), Is.False);
        Assert.That((await debug.InspectAsync()).CachedGroups, Is.EqualTo(new[] { "main" }));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await debug.ResumeAsync(generation));
    }

    [Test]
    public async Task LatestNaturalReentryReplacesTheSingleEntrance()
    {
        var graph = GraphOf(Entry("test.text", "first"), Entry("test.scene", "object"), Entry("test.text", "last"));
        graph.Edges.Add(new Edge("main", 0, "main"));
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait())
            .RegisterInstruction("test.scene", context => new ImmediatePrimitiveInstance(() =>
                context.Runtime.State.Table<Layer>().Set(new Layer { Id = "existing", AssetId = "second-entrance" }))).BuildView();
        await using var engine = new GameEngine(graph, Runtime(), view);
        using var debug = engine.AttachDebugging(Options());
        await engine.AdvanceAsync();
        await engine.AdvanceAsync();
        await engine.AdvanceAsync();
        Assert.That(engine.EntryIndex, Is.EqualTo(1));
        await debug.SeekAsync(debug.Generation, new("main", 0, []));
        Assert.That(engine.Runtime.State.Table<Layer>().Get("existing").AssetId, Is.EqualTo("second-entrance"));
        Assert.That((await debug.InspectAsync()).CachedGroups, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task ExplicitRouteIgnoresConditionsAndChoiceWithoutWritingVariables()
    {
        var graph = GraphOf(Entry("test.text", "first"), Entry("test.scene", "conditional", "false"), Entry("test.write", "write"));
        graph.Nodes.Add(new Branch { Id = "choice", BranchType = BranchType.Choice });
        graph.Nodes.Add(new Group { Id = "target", Entries = { Entry("test.text", "target") } });
        graph.Edges.AddRange([new Edge("main", 0, "choice"), new Edge("choice", 7, "target")]);
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait())
            .RegisterInstruction("test.write", context => new ImmediatePrimitiveInstance(() => context.Runtime.SetVariable("save.value", 99)))
            .RegisterInstruction("test.scene", context => new ImmediatePrimitiveInstance(() =>
            {
                context.Runtime.SetVariable("save.value", 88);
                context.Runtime.State.Table<Layer>().Set(new Layer { Id = "bg", AssetId = "forced" });
            })).BuildView();
        await using var engine = new GameEngine(graph, Runtime(), view);
        engine.Runtime.SetVariable("save.value", 3);
        using var debug = engine.AttachDebugging(Options());
        await engine.AdvanceAsync();
        await debug.SeekAsync(debug.Generation, new("target", 0, [new("main", 0, "choice"), new("choice", 7, "target")]));
        Assert.That(engine.CurrentNodeId, Is.EqualTo("target"));
        Assert.That(engine.EntryIndex, Is.Zero);
        Assert.That(engine.Runtime.State.Table<Layer>().Get("bg").AssetId, Is.EqualTo("forced"));
        Assert.That(engine.Runtime.GetVariable("value")!.AsInt(), Is.EqualTo(3));
        Assert.That((await debug.InspectAsync()).Paused, Is.True);
    }

    [Test]
    public async Task InvalidRouteOrUnsupportedInstructionDoesNotDestroyTheCurrentRun()
    {
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait())
            .RegisterInstruction("test.scene", _ => new ImmediatePrimitiveInstance(() => { })).BuildView();
        await using var engine = new GameEngine(GraphOf(Entry("test.text", "first"), Entry("test.scene", "unknown")), Runtime(), view);
        using var debug = engine.AttachDebugging();
        await engine.AdvanceAsync();
        Assert.ThrowsAsync<NotSupportedException>(async () => await debug.SeekAsync(debug.Generation, new("main", 2, [])));
        Assert.ThrowsAsync<ArgumentException>(async () => await debug.SeekAsync(debug.Generation, new("main", 0, [new("main", 1, "main")])));
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Running));
        Assert.That((await debug.InspectAsync()).Paused, Is.False);
        Assert.That(engine.EntryIndex, Is.EqualTo(1));
    }

    [Test]
    public async Task PausingStopsNaturalContinuationAndPlayerAdvanceUntilResume()
    {
        var wait = new NaturalWait();
        var calls = 0;
        using var view = Registration().RegisterInstruction("test.text", _ => wait)
            .RegisterInstruction("test.scene", _ => new ImmediatePrimitiveInstance(() => calls++)).BuildView();
        await using var engine = new GameEngine(GraphOf(Entry("test.text", "first"), Entry("test.scene", "after")), Runtime(), view);
        using var debug = engine.AttachDebugging();
        await engine.AdvanceAsync();
        await debug.PauseAsync(debug.Generation);
        wait.Finish();
        await engine.AdvanceAsync();
        Assert.That(calls, Is.Zero);
        await debug.ResumeAsync(debug.Generation);
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public async Task VariablesAreCopiedAndWritesCheckTheObservedValueTypeAndGeneration()
    {
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait()).BuildView();
        await using var engine = new GameEngine(GraphOf(Entry("test.text", "first")), Runtime(), view);
        using var debug = engine.AttachDebugging();
        engine.Runtime.SetVariable("save.value", 4);
        var before = await debug.InspectAsync();
        var variable = before.Variables.Single();
        await debug.SetVariableAsync(before.Generation, variable, "9");
        Assert.That(engine.Runtime.GetVariable("value")!.AsInt(), Is.EqualTo(9));
        Assert.That(variable.Value, Is.EqualTo("4"));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await debug.SetVariableAsync(before.Generation, variable, "10"));
        var current = (await debug.InspectAsync()).Variables.Single();
        Assert.ThrowsAsync<FormatException>(async () => await debug.SetVariableAsync(before.Generation, current, "not-an-int"));
        engine.RestoreFrom(engine.Runtime.CreateSnapshot());
        Assert.ThrowsAsync<InvalidOperationException>(async () => await debug.SetVariableAsync(before.Generation, current, "11"));
    }

    private static GameRuntime Runtime()
    {
        var builder = new StateCatalogBuilder();
        builder.Register<Layer>("layers", layer => layer.Id);
        return new(null, rootNodeId: "main", stateCatalog: builder.Build());
    }
    private static GameDebugOptions Options() => new()
    {
        Instructions = new Dictionary<string, DebugReplayBehavior>
        {
            ["test.text"] = DebugReplayBehavior.Scene, ["test.scene"] = DebugReplayBehavior.Scene, ["test.write"] = DebugReplayBehavior.Ignore
        }
    };
    private static GameRegistration Registration() => new GameRegistration().RegisterSchema(new DefaultEntryModule("test",
        [new DefaultPrimitiveEntryBase("test.text", DynamicParameterTable.Empty) { IsReading = true },
         new DefaultPrimitiveEntryBase("test.scene", DynamicParameterTable.Empty), new DefaultPrimitiveEntryBase("test.write", DynamicParameterTable.Empty)]));
    private static PrimitiveEntry Entry(string type, string id, string condition = "") => new(type, JsonSerializer.SerializeToElement(new { })) { StableId = id, Condition = condition };
    private static Graph GraphOf(params Entry[] entries) => new() { RootNodeId = "main", Nodes = { new Group { Id = "main", Entries = entries.ToList() } } };
    private sealed class TextWait : PrimitiveInstance
    {
        public override bool IsBlocking => true;
        public override bool IsSkippable => true;
        protected override void OnDispatch() { }
        protected override void OnPlaybackModeChanged(PrimitiveInvocationMode mode) { if (mode == PrimitiveInvocationMode.CatchUp) TryComplete(); }
        protected override void OnSkip() => TryComplete();
    }
    private sealed class NaturalWait : PrimitiveInstance
    {
        public override bool IsBlocking => true;
        public override bool IsSkippable => false;
        protected override void OnDispatch() { }
        public void Finish() => TryComplete();
    }
    private sealed class ReplayBlocker : PrimitiveInstance
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool IsBlocking => true;
        public override bool IsSkippable => false;
        protected override void OnDispatch() => Entered.TrySetResult();
    }
}
