using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Core.Variable;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Progress;
using GalNet.Runtime.Runtime;

namespace GeneralTest.Runtime;

public class DebugSessionTests
{
    [Test]
    public async Task CompressingTheCurrentUnreadTextForDebugSeekDoesNotMarkItRead()
    {
        var wait = new DeferredReadingWait();
        var progress = new ReadCounter();
        using var view = Registration().RegisterInstruction("test.text", _ => wait).BuildView();
        await using var engine = new GameEngine(GraphOf(Entry("test.text", "unread")), Runtime(), view, progress: progress);
        using var debug = engine.AttachDebugging(Options());
        await engine.AdvanceAsync();
        var seek = debug.SeekAsync(debug.Generation, new("main", 1, []));
        try
        {
            await wait.Compressed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while ((await debug.InspectAsync(timeout.Token)).ActiveEntryIds.Length != 0) await Task.Delay(1, timeout.Token);
        }
        finally { wait.Presented.TrySetResult(); }
        await seek;
        Assert.That(progress.Marks, Is.Zero);
    }
    [Test]
    public async Task ForwardSeekCapturesTheStateCommittedByAnActiveTransitionDuringCompression()
    {
        using var view = Registration().RegisterInstruction("test.scene", context => new CompressingWait(context.Runtime))
            .RegisterInstruction("test.text", _ => new TextWait()).BuildView();
        await using var engine = new GameEngine(GraphOf(Entry("test.scene", "transition"), Entry("test.text", "target")), Runtime(), view);
        using var debug = engine.AttachDebugging(Options());
        await engine.AdvanceAsync();
        Assert.That(engine.Runtime.State.Table<Layer>().Get("scene").AssetId, Is.EqualTo("initial"));
        await debug.SeekAsync(debug.Generation, new("main", 1, []));
        Assert.That(engine.Runtime.State.Table<Layer>().Get("scene").AssetId, Is.EqualTo("compressed"));
    }
    [TestCase(0, 0)]
    [TestCase(256, 1)]
    public async Task EntranceCacheHonorsItsCountAndPayloadBudgets(int groups, long bytes)
    {
        var graph = GraphOf(Entry("test.text", "first"));
        graph.Nodes.Add(new Group { Id = "second", Entries = { Entry("test.text", "second") } });
        graph.Edges.Add(new("main", 0, "second"));
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait()).BuildView();
        await using var engine = new GameEngine(graph, Runtime(), view);
        using var debug = engine.AttachDebugging(new() { Instructions = Options().Instructions, MaxEntranceGroups = groups, MaxEntrancePayloadBytes = bytes });
        await engine.AdvanceAsync(); await engine.AdvanceAsync();
        var state = await debug.InspectAsync();
        Assert.That(state.CachedGroups, Is.Empty);
        Assert.That(state.Cache!.PayloadBytes, Is.Zero);
    }

    [Test]
    public async Task LatestEntranceLruAvoidsReplayingEarlierGroupsAndReportsMeasuredWork()
    {
        var graph = GraphOf(Enumerable.Range(0, 2000).Select(index => Entry("test.scene", "prefix" + index)).Append(Entry("test.text", "wait")).ToArray());
        graph.Nodes.Add(new Group { Id = "second", Entries = Enumerable.Range(0, 200).Select(index => (Entry)Entry("test.scene", "local" + index)).Append(Entry("test.text", "second")).ToList() });
        graph.Edges.Add(new("main", 0, "second"));
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait())
            .RegisterInstruction("test.scene", context => new ImmediatePrimitiveInstance(() => context.Runtime.State.Table<Layer>()
                .Set(new Layer { Id = "scene", AssetId = context.Entry.StableId }))).BuildView();
        await using var engine = new GameEngine(graph, Runtime(), view);
        using var debug = engine.AttachDebugging(new() { Instructions = Options().Instructions, MaxEntranceGroups = 1 });
        await engine.AdvanceAsync(); await engine.AdvanceAsync();
        Assert.That((await debug.InspectAsync()).CachedGroups, Is.EqualTo(new[] { "second" }));
        await debug.SeekAsync(debug.Generation, new("second", 150, []));
        var local = await debug.InspectAsync();
        Assert.That(local.Replay!.EntranceCacheHit, Is.True);
        Assert.That(local.Replay.Instructions, Is.EqualTo(150));
        var result = engine.Runtime.State.Table<Layer>().Get("scene").AssetId;
        var expectedStates = JsonSerializer.Serialize(engine.Runtime.CreateSnapshot().States);
        await debug.SeekAsync(debug.Generation, new("second", 150, [new("main", 0, "second")]));
        var root = await debug.InspectAsync();
        Assert.That(root.Replay!.EntranceCacheHit, Is.False);
        Assert.That(root.Replay.Instructions, Is.EqualTo(2151));
        Assert.That(engine.Runtime.State.Table<Layer>().Get("scene").AssetId, Is.EqualTo(result));
        Assert.That(JsonSerializer.Serialize(engine.Runtime.CreateSnapshot().States), Is.EqualTo(expectedStates));
        Assert.That(root.Cache!.PayloadBytes, Is.LessThanOrEqualTo(root.Cache.BudgetBytes));
        TestContext.Out.WriteLine($"entrance={local.Replay.Milliseconds:F2}ms/{local.Replay.Instructions} primitives; root={root.Replay.Milliseconds:F2}ms/{root.Replay.Instructions} primitives; payload={root.Cache.PayloadBytes} bytes");
        engine.RestoreFrom(engine.Runtime.CreateSnapshot());
        Assert.That((await debug.InspectAsync()).Cache!.PayloadBytes, Is.Zero);
    }

    [Test]
    public async Task TargetAuthorConditionIsForcedOnceAcrossExpansionThenOrdinaryConditionsResume()
    {
        var calls = new List<string>();
        using var view = Registration().RegisterInstruction("test.scene", context => new ImmediatePrimitiveInstance(() => calls.Add(context.Entry.StableId)))
            .RegisterInstruction("test.text", _ => new TextWait()).BuildView();
        var graph = GraphOf(Entry("test.scene", "author#1", "false"), Entry("test.scene", "author#2", "false"),
            Entry("test.scene", "following#1", "false"), Entry("test.text", "wait"));
        await using var engine = new GameEngine(graph, Runtime(), view);
        using var debug = engine.AttachDebugging(Options());
        await debug.SeekAsync(debug.Generation, new("main", 0, [], TargetEnd: 2));
        await debug.ResumeAsync(debug.Generation);
        Assert.That(calls, Is.EqualTo(new[] { "author#1", "author#2" }));
        await engine.AdvanceAsync();
        Assert.That(calls, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task BreakpointsStopBeforeAuthorSpansAndStepDoesNotRunFollowingEntry()
    {
        var calls = 0;
        using var view = Registration().RegisterInstruction("test.scene", _ => new ImmediatePrimitiveInstance(() => calls++))
            .RegisterInstruction("test.text", _ => new TextWait()).BuildView();
        var graph = GraphOf(Entry("test.scene", "first#1"), Entry("test.scene", "first#2"), Entry("test.scene", "next#1"), Entry("test.text", "wait"));
        await using var engine = new GameEngine(graph, Runtime(), view);
        using var debug = engine.AttachDebugging(Options());
        await debug.SetBreakpointsAsync(debug.Generation, [new("main", 0, 2)]);
        await engine.AdvanceAsync();
        Assert.That(calls, Is.Zero);
        Assert.That((await debug.InspectAsync()).Paused, Is.True);
        await debug.StepAsync(debug.Generation, new("main", 0, 2));
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(engine.EntryIndex, Is.EqualTo(2));
        Assert.That((await debug.InspectAsync()).Paused, Is.True);
        await debug.ResumeAsync(debug.Generation);
        Assert.That(calls, Is.EqualTo(3));
    }

    [Test]
    public async Task CrossGroupReplayCannotReplaceNaturalEntrancesAndIsolatedStartUsesInitialScene()
    {
        var graph = GraphOf(Entry("test.text", "first"), Entry("test.scene", "object", "false"));
        graph.Nodes.Add(new Group { Id = "second", Entries = { Entry("test.text", "second-line") } });
        graph.Nodes.Add(new Group { Id = "isolated", Entries = { Entry("test.text", "isolated-line") } });
        graph.Edges.Add(new("main", 0, "second"));
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait())
            .RegisterInstruction("test.scene", context => new ImmediatePrimitiveInstance(() => context.Runtime.State.Table<Layer>()
                .Set(new Layer { Id = "forced", AssetId = "replay" }))).BuildView();
        await using var engine = new GameEngine(graph, Runtime(), view);
        using var debug = engine.AttachDebugging(Options());
        await engine.AdvanceAsync(); await engine.AdvanceAsync();
        Assert.That(engine.CurrentNodeId, Is.EqualTo("second"));
        await debug.SeekAsync(debug.Generation, new("main", 0, []));
        await debug.SeekAsync(debug.Generation, new("second", 0, [new("main", 0, "second")]));
        Assert.That(engine.Runtime.State.Table<Layer>().TryGet("forced", out _), Is.True);
        await debug.ResumeAsync(debug.Generation);
        await debug.SeekAsync(debug.Generation, new("second", 0, []));
        Assert.That(engine.Runtime.State.Table<Layer>().TryGet("forced", out _), Is.False);
        await debug.SeekAsync(debug.Generation, new("isolated", 0, [], InitialScene: true));
        Assert.That(engine.CurrentNodeId, Is.EqualTo("isolated"));
        Assert.That(engine.Runtime.State.Table<Layer>().TryGet("forced", out _), Is.False);
    }

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
        Assert.CatchAsync<OperationCanceledException>(async () => await operation.WaitAsync(TimeSpan.FromSeconds(2)));
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
            ["test.text"] = DebugReplayBehavior.Scene,
            ["test.scene"] = DebugReplayBehavior.Scene,
            ["test.write"] = DebugReplayBehavior.Ignore
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
    private sealed class CompressingWait(IGameRuntime runtime) : PrimitiveInstance
    {
        public override bool IsBlocking => true;
        public override bool IsSkippable => false;
        protected override void OnDispatch() => runtime.State.Table<Layer>().Set(new Layer { Id = "scene", AssetId = "initial" });
        protected override void OnPlaybackModeChanged(PrimitiveInvocationMode mode)
        {
            if (mode != PrimitiveInvocationMode.CatchUp) return;
            runtime.State.Table<Layer>().Set(new Layer { Id = "scene", AssetId = "compressed" });
            TryComplete();
        }
    }
    private sealed class ReadCounter : IGameProgressService
    {
        public int Marks;
        public bool IsRead(string groupId, string entryId) => false;
        public void MarkRead(string groupId, string entryId) => Interlocked.Increment(ref Marks);
    }
    private sealed class DeferredReadingWait : PrimitiveInstance, IAdvanceWaitInstance
    {
        public TaskCompletionSource Compressed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Presented { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task PresentationCompletion => Presented.Task;
        public override bool IsBlocking => true;
        public override bool IsSkippable => true;
        public bool IsTextPresented { get; private set; }
        public bool IsWaitingForAdvance => false;
        public void CompleteAdvance() => TryComplete();
        protected override void OnDispatch() => SetAutoBlocking(true);
        protected override void OnPlaybackModeChanged(PrimitiveInvocationMode mode)
        {
            if (mode != PrimitiveInvocationMode.CatchUp) return;
            IsTextPresented = true;
            SetAutoBlocking(false);
            TryComplete();
            Compressed.TrySetResult();
        }
    }
}
