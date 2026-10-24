using System.Collections.Concurrent;
using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Engine;

namespace GeneralTest.Runtime;

public class GroupEndCatchUpTests
{
    [Test]
    public async Task UnreadLinesAreConsumedButSuccessorIsNotDispatchedUntilAdvance()
    {
        var modes = new ConcurrentQueue<PrimitiveInvocationMode>();
        var successorCalls = 0;
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait(modes.Enqueue))
            .RegisterInstruction("test.action", _ => new ImmediatePrimitiveInstance(() => successorCalls++)).BuildView();
        var graph = GraphOf(Entry("test.text", "first"), Entry("test.text", "second"));
        graph.Nodes.Add(new Group { Id = "next", Entries = { Entry("test.action", "next") } });
        graph.Edges.Add(new Edge("main", 0, "next"));
        await using var engine = new GameEngine(graph, view);
        await engine.AdvanceAsync();

        await engine.SkipToGroupEndAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Multiple(() =>
        {
            Assert.That(modes, Is.EqualTo(new[] { PrimitiveInvocationMode.Normal, PrimitiveInvocationMode.CatchUp }));
            Assert.That(successorCalls, Is.Zero);
            Assert.That(engine.CurrentNodeId, Is.EqualTo("main"));
            Assert.That(engine.EntryIndex, Is.EqualTo(2));
            Assert.That(engine.IsRunning, Is.True);
            Assert.That(engine.IsCatchingUp, Is.False);
            Assert.That(engine.CreateSaveData().EntryIndex, Is.EqualTo(2));
        });
        await engine.SkipToGroupEndAsync();
        Assert.That(successorCalls, Is.Zero);
        await engine.AdvanceAsync();
        Assert.That(successorCalls, Is.EqualTo(1));
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Completed));
    }

    [TestCase(false, 1)]
    [TestCase(true, 2)]
    public async Task ConditionPolicyIsExplicitAndDoesNotChangeLaterNormalExecution(bool ignoreConditions, int expected)
    {
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait())
            .RegisterInstruction("test.action", context => new ImmediatePrimitiveInstance(() =>
                context.Runtime.SetVariable("count", (context.Runtime.GetVariable("count")?.AsInt() ?? 0) + 1))).BuildView();
        var graph = GraphOf(Entry("test.text", "first"), Entry("test.action", "conditional", "false"), Entry("test.action", "unconditional"));
        graph.Nodes.Add(new Group { Id = "next", Entries = { Entry("test.action", "next", "false"), Entry("test.text", "last") } });
        graph.Edges.Add(new Edge("main", 0, "next"));
        await using var engine = new GameEngine(graph, view);
        await engine.AdvanceAsync();
        await engine.SkipToGroupEndAsync(ignoreConditions);
        Assert.That(engine.Runtime.GetVariable("count")!.AsInt(), Is.EqualTo(expected));
        await engine.AdvanceAsync();
        Assert.That(engine.Runtime.GetVariable("count")!.AsInt(), Is.EqualTo(expected));
        Assert.That(engine.CurrentNodeId, Is.EqualTo("next"));
    }

    [Test]
    public async Task GroupEndDoesNotEvaluateOrPresentTheFollowingChoice()
    {
        var choices = new Choices();
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait()).BuildView();
        var graph = GraphOf(Entry("test.text", "first"));
        graph.Nodes.Add(new Branch { Id = "choice", BranchType = BranchType.Choice, Options = { new BranchOption { Text = "A", Condition = "true" } } });
        graph.Edges.Add(new Edge("main", 0, "choice"));
        await using var engine = new GameEngine(graph, view, choicePresenter: choices);
        await engine.AdvanceAsync();
        await engine.SkipToGroupEndAsync();
        Assert.That(choices.Calls, Is.Zero);
        await engine.AdvanceAsync();
        Assert.That(choices.Calls, Is.EqualTo(1));
        await engine.SkipToGroupEndAsync();
        Assert.That(choices.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task GroupEndDoesNotFollowASelfLoop()
    {
        var actions = 0;
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait())
            .RegisterInstruction("test.action", _ => new ImmediatePrimitiveInstance(() => actions++)).BuildView();
        var graph = GraphOf(Entry("test.text", "first"), Entry("test.action", "action"));
        graph.Edges.Add(new Edge("main", 0, "main"));
        await using var engine = new GameEngine(graph, view);
        await engine.AdvanceAsync();
        await engine.SkipToGroupEndAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(actions, Is.EqualTo(1));
        await engine.AdvanceAsync();
        Assert.That(engine.EntryIndex, Is.EqualTo(1));
        Assert.That(actions, Is.EqualTo(1));
    }

    [Test]
    public async Task EmptyRestoredGroupCanStopAtItsEndWithoutFollowingAnEdge()
    {
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait()).BuildView();
        var graph = GraphOf();
        graph.Nodes.Add(new Group { Id = "next", Entries = { Entry("test.text", "next") } });
        graph.Edges.Add(new Edge("main", 0, "next"));
        await using var engine = new GameEngine(graph, view);
        engine.RestoreFrom(engine.CreateSaveData());
        await engine.SkipToGroupEndAsync();
        Assert.That(engine.CurrentNodeId, Is.EqualTo("main"));
        Assert.That(engine.EntryIndex, Is.Zero);
        await engine.AdvanceAsync();
        Assert.That(engine.CurrentNodeId, Is.EqualTo("next"));
    }

    [Test]
    public async Task EndOfLastGroupIsNotTerminalUntilAdvance()
    {
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait()).BuildView();
        await using var engine = new GameEngine(GraphOf(Entry("test.text", "first")), view);
        await engine.SkipToGroupEndAsync();
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Ready));
        await engine.AdvanceAsync();
        await engine.SkipToGroupEndAsync();
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Running));
        await engine.AdvanceAsync();
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Completed));
        await engine.SkipToGroupEndAsync();
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Completed));
    }

    [Test]
    public async Task ExistingPresentationIsDrainedAndFinalSceneIsSynchronizedBeforeReturn()
    {
        var old = new DelayedCleanup();
        var port = new Synchronization();
        var calls = 0;
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait())
            .RegisterInstruction("test.action", _ => ++calls == 1 ? old : new ImmediatePrimitiveInstance(() => { })).BuildView();
        await using var engine = new GameEngine(GraphOf(Entry("test.action", "old"), Entry("test.text", "first"), Entry("test.action", "after")),
            view, catchUpPresentation: port);
        await engine.AdvanceAsync();
        var operation = engine.SkipToGroupEndAsync();
        await old.Detached.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(calls, Is.EqualTo(1));
        old.Cleaned.TrySetResult();
        await port.EndEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(operation.IsCompleted, Is.False);
        Assert.That(calls, Is.EqualTo(2));
        port.EndReleased.TrySetResult();
        await operation.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(engine.EntryIndex, Is.EqualTo(3));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task WaitingForAnUnsupportedBlockerCanBeCancelledOrStopped(bool stop)
    {
        var blocker = new UnsupportedBlocker();
        using var view = Registration().RegisterInstruction("test.action", _ => blocker)
            .RegisterInstruction("test.text", _ => new TextWait()).BuildView();
        await using var engine = new GameEngine(GraphOf(Entry("test.action", "blocker"), Entry("test.text", "after")), view);
        await engine.AdvanceAsync();
        var operation = engine.SkipToGroupEndAsync();
        await blocker.CatchUpEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(engine.SkipToGroupEndAsync(), Is.SameAs(operation));
        Assert.Throws<InvalidOperationException>(() => engine.SkipToUnreadAsync());
        if (stop) await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
        else engine.CancelCatchUp();
        await operation.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(engine.IsCatchingUp, Is.False);
        Assert.That(engine.EntryIndex, Is.EqualTo(1));
        Assert.That(engine.State, Is.EqualTo(stop ? GameEngineState.Stopped : GameEngineState.Running));
    }

    [Test]
    public async Task FailureIsReportedAndStopStillDrains()
    {
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait())
            .RegisterInstruction("test.action", _ => new ImmediatePrimitiveInstance(() => throw new InvalidOperationException("broken entry"))).BuildView();
        await using var engine = new GameEngine(GraphOf(Entry("test.text", "first"), Entry("test.action", "bad")), view);
        await engine.AdvanceAsync();
        var error = Assert.ThrowsAsync<InvalidOperationException>(async () => await engine.SkipToGroupEndAsync());
        Assert.That(error!.Message, Is.EqualTo("broken entry"));
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Faulted));
        await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static GameRegistration Registration() => new GameRegistration().RegisterSchema(new DefaultEntryModule("test",
        [new DefaultPrimitiveEntryBase("test.text", DynamicParameterTable.Empty) { IsReading = true },
         new DefaultPrimitiveEntryBase("test.action", DynamicParameterTable.Empty)]));
    private static PrimitiveEntry Entry(string type, string id, string condition = "") =>
        new(type, JsonSerializer.SerializeToElement(new { })) { StableId = id, Condition = condition };
    private static Graph GraphOf(params Entry[] entries) => new()
    {
        RootNodeId = "main",
        Nodes = { new Group { Id = "main", Entries = entries.ToList() } }
    };
    private sealed class TextWait(Action<PrimitiveInvocationMode>? record = null) : PrimitiveInstance
    {
        public override bool IsBlocking => true;
        public override bool IsSkippable => true;
        protected override void OnDispatch() => record?.Invoke(InvocationMode);
        protected override void OnPlaybackModeChanged(PrimitiveInvocationMode mode)
        {
            if (mode == PrimitiveInvocationMode.CatchUp) TryComplete();
        }
        protected override void OnSkip() => TryComplete();
    }
    private sealed class UnsupportedBlocker : PrimitiveInstance
    {
        public TaskCompletionSource CatchUpEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool IsBlocking => true;
        public override bool IsSkippable => false;
        protected override void OnDispatch() { }
        protected override void OnPlaybackModeChanged(PrimitiveInvocationMode mode)
        {
            if (mode == PrimitiveInvocationMode.CatchUp) CatchUpEntered.TrySetResult();
        }
    }
    private sealed class DelayedCleanup : PrimitiveInstance
    {
        public TaskCompletionSource Detached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cleaned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool IsBlocking => false;
        public override bool IsSkippable => false;
        public override Task PresentationCompletion => Cleaned.Task;
        protected override void OnDispatch() { }
        protected override void OnPlaybackModeChanged(PrimitiveInvocationMode mode)
        {
            if (mode != PrimitiveInvocationMode.CatchUp) return;
            TryComplete();
            Detached.TrySetResult();
        }
    }
    private sealed class Synchronization : ICatchUpPresentation
    {
        public TaskCompletionSource EndEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource EndReleased { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task BeginCatchUpAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EndCatchUpAsync(IGameRuntime runtime, CancellationToken cancellationToken)
        {
            EndEntered.TrySetResult();
            return EndReleased.Task.WaitAsync(cancellationToken);
        }
    }
    private sealed class Choices : IChoicePresenter
    {
        public int Calls { get; private set; }
        public Task<int> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellationToken)
        {
            Calls++;
            return new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }
    }
}
