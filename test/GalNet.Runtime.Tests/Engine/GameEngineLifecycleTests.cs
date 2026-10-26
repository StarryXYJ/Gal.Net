using System.Collections.Concurrent;
using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Progress;

namespace GeneralTest.Runtime;

public sealed class GameEngineLifecycleTests
{
    [Test]
    public async Task ObsoleteCancellationCallbackFailureCannotLeaveRestorationHalfApplied()
    {
        var calls = 0;
        using var view = View(context =>
        {
            if (++calls == 1) context.ScopeCancellation.Register(() => throw new IOException("obsolete callback"));
            return new Blocker();
        });
        await using var engine = new GameEngine(GroupGraph(), view);
        var saved = engine.CreateSaveData();
        await engine.AdvanceAsync();
        await engine.RestoreFromAsync(saved);
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Ready));
        await engine.AdvanceAsync();
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Running));
    }

    [Test]
    public async Task InstructionsReceiveGameplayContextWithoutEngineRestorationOrCursorControl()
    {
        PrimitiveCreateContext? context = null;
        using var view = View(value => { context = value; return new Blocker(); });
        await using var engine = new GameEngine(GroupGraph(), view);
        var saved = engine.CreateSaveData();
        await engine.AdvanceAsync();
        Assert.That(context!.Runtime, Is.Not.InstanceOf<IGameRuntime>());
        var oldContext = context.Runtime;
        await engine.RestoreFromAsync(saved);
        engine.Runtime.SetVariable("value", 42);
        Assert.Throws<OperationCanceledException>(() => oldContext.SetVariable("value", 99));
        Assert.That(engine.Runtime.GetVariable("value")!.AsInt(), Is.EqualTo(42));
    }

    [Test]
    public async Task HostMutationsCannotChangeAnExistingRunGraph()
    {
        var dispatched = 0;
        using var view = View(_ => new ImmediatePrimitiveInstance(() => dispatched++));
        var graph = GroupGraph();
        await using var engine = new GameEngine(graph, view);
        ((PrimitiveEntry)((Group)graph.Nodes[0]).Entries[0]).Condition = "false";
        ((Group)graph.Nodes[0]).Entries.Clear();
        graph.Nodes.Clear();
        graph.RootNodeId = "changed";
        await engine.AdvanceAsync();
        Assert.That(dispatched, Is.EqualTo(1));
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Completed));
    }

    [TestCase("node")]
    [TestCase("edge")]
    [TestCase("root")]
    [TestCase("target")]
    [TestCase("outlet")]
    public void InvalidGraphIsRejectedBeforeExecution(string fault)
    {
        using var view = View(_ => new ImmediatePrimitiveInstance());
        var graph = GroupGraph();
        switch (fault)
        {
            case "node": graph.Nodes.Add(new Group { Id = "main" }); break;
            case "edge": graph.Edges.Add(new("main", 0, "main")); graph.Edges.Add(new("main", 0, "main")); break;
            case "root": graph.RootNodeId = "missing"; break;
            case "target": graph.Edges.Add(new("main", 0, "missing")); break;
            case "outlet": graph.Edges.Add(new("main", 1, "main")); break;
        }
        Assert.Throws<InvalidDataException>(() => new GameEngine(graph, view));
    }

    [Test]
    public async Task CheckpointObserversCanRestoreAndFailuresDoNotFaultStory()
    {
        using var view = View(_ => new ImmediatePrimitiveInstance());
        await using var engine = new GameEngine(GroupGraph(), view);
        var initial = engine.CreateSaveData();
        Task? restored = null;
        engine.CheckpointCreated += _ => throw new IOException("observer failed");
        engine.CheckpointCreated += _ => restored ??= engine.RestoreFromAsync(initial);
        await engine.AdvanceAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await restored!.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Ready));
    }

    [TestCase("missing", 0)]
    [TestCase("main", -1)]
    [TestCase("main", 5)]
    public async Task InvalidSavePositionLeavesCurrentScopeIntact(string node, int index)
    {
        PrimitiveCreateContext? context = null;
        using var view = View(value => { context = value; return new Blocker(); });
        await using var engine = new GameEngine(GroupGraph(), view);
        await engine.AdvanceAsync();
        var state = engine.Runtime.State;
        Assert.ThrowsAsync<InvalidDataException>(() => engine.RestoreFromAsync(new GameSnapshot
        { NodeId = node, EntryIndex = index, States = engine.CreateSaveData().States }));
        Assert.That(engine.Runtime.State, Is.SameAs(state));
        Assert.That(context!.ScopeCancellation.IsCancellationRequested, Is.False);
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Running));
    }

    [Test]
    public async Task NonblockingCycleYieldsAndCallerCancellationKeepsRunUsable()
    {
        using var view = View(_ => new ImmediatePrimitiveInstance());
        var graph = GroupGraph();
        graph.Edges.Add(new Edge("main", 0, "main"));
        await using var engine = new GameEngine(graph, view);
        using var cancellation = new CancellationTokenSource();
        engine.CheckpointCreated += _ => cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await engine.AdvanceAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Running));
        await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Stopped));
    }

    [Test]
    public async Task CompletionIsReportedOnceAndFurtherAdvanceDoesNotReplayContent()
    {
        var calls = 0;
        using var view = View(_ => new ImmediatePrimitiveInstance(() => calls++));
        await using var engine = new GameEngine(GroupGraph(), view);
        var states = new List<GameEngineState>();
        engine.StateChanged += change => states.Add(change.State);

        Assert.That(engine.State, Is.EqualTo(GameEngineState.Ready));
        await engine.AdvanceAsync();
        await engine.AdvanceAsync();

        Assert.Multiple(() =>
        {
            Assert.That(states, Is.EqualTo(new[] { GameEngineState.Running, GameEngineState.Completed }));
            Assert.That(calls, Is.EqualTo(1));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NaturalBlockerCompletionReportsTheTerminalStateWithoutAnotherClick(bool fail)
    {
        var blocker = new Blocker();
        var calls = 0;
        using var view = View(_ => ++calls == 1 ? blocker : fail
            ? throw new InvalidOperationException("continuation failed")
            : new ImmediatePrimitiveInstance());
        await using var engine = new GameEngine(GroupGraph(2), view);
        var terminal = new TaskCompletionSource<GameEngineStateChange>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.StateChanged += change =>
        {
            if (change.State is GameEngineState.Completed or GameEngineState.Faulted) terminal.TrySetResult(change);
        };
        await engine.AdvanceAsync();

        blocker.Finish();
        var result = await terminal.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(result.State, Is.EqualTo(fail ? GameEngineState.Faulted : GameEngineState.Completed));
        if (fail) Assert.That(result.Error!.Message, Is.EqualTo("continuation failed"));
    }

    [TestCase(0, GameEngineState.Completed)]
    [TestCase(7, GameEngineState.Faulted)]
    public async Task ChoiceResultReportsTheTerminalState(int selected, GameEngineState expected)
    {
        var choice = new PendingChoicePresenter();
        using var view = View(_ => new ImmediatePrimitiveInstance());
        await using var engine = new GameEngine(ChoiceGraph(), view, choicePresenter: choice);
        var terminal = new TaskCompletionSource<GameEngineStateChange>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.StateChanged += change =>
        {
            if (change.State is GameEngineState.Completed or GameEngineState.Faulted) terminal.TrySetResult(change);
        };
        await engine.AdvanceAsync();

        choice.Result.SetResult(selected);

        var result = await terminal.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(result.State, Is.EqualTo(expected));
        Assert.That(result.Error is not null, Is.EqualTo(expected == GameEngineState.Faulted));
    }

    [Test]
    public async Task ChoiceFailureIsReportedImmediately()
    {
        var choice = new PendingChoicePresenter();
        using var view = View(_ => new ImmediatePrimitiveInstance());
        await using var engine = new GameEngine(ChoiceGraph(), view, choicePresenter: choice);
        var fault = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.StateChanged += change => { if (change.Error is { } error) fault.TrySetResult(error); };
        await engine.AdvanceAsync();

        choice.Result.SetException(new InvalidOperationException("choice failed"));

        Assert.That((await fault.Task.WaitAsync(TimeSpan.FromSeconds(2))).Message, Is.EqualTo("choice failed"));
    }

    [Test]
    public async Task StopCancelsAndDrainsEvenAnUncooperativeChoicePresenter()
    {
        var choice = new PendingChoicePresenter();
        using var view = View(_ => new ImmediatePrimitiveInstance());
        await using var engine = new GameEngine(ChoiceGraph(), view, choicePresenter: choice);
        var states = new ConcurrentQueue<GameEngineState>();
        engine.StateChanged += change => states.Enqueue(change.State);
        await engine.AdvanceAsync();

        await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
        choice.Result.SetResult(0);
        await engine.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(choice.Token.IsCancellationRequested, Is.True);
            Assert.That(engine.IsRunning, Is.False);
            Assert.That(states, Is.EqualTo(new[] { GameEngineState.Running, GameEngineState.Stopped }));
            Assert.That(engine.CurrentNodeId, Is.EqualTo("choice"));
        });
        Assert.That(await engine.AdvanceAsync(), Is.False);
        Assert.ThrowsAsync<InvalidOperationException>(() => engine.RestoreFromAsync(engine.CreateSaveData()));
    }

    [Test]
    public async Task StopWaitsForInFlightAdvanceAndPreventsQueuedAndFollowingInstructions()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var calls = 0;
        CancellationToken scope = default;
        using var view = View(context => new ImmediatePrimitiveInstance(() =>
        {
            scope = context.ScopeCancellation;
            Interlocked.Increment(ref calls);
            entered.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException();
        }));
        await using var engine = new GameEngine(GroupGraph(2), view);
        var advance = Task.Run(() => engine.AdvanceAsync());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var queued = engine.AdvanceAsync();
        var stop = engine.StopAsync();
        try
        {
            Assert.That(stop.IsCompleted, Is.False);
            Assert.That(SpinWait.SpinUntil(() => scope.IsCancellationRequested, TimeSpan.FromSeconds(2)), Is.True);
        }
        finally { release.Set(); }

        await Task.WhenAll(advance, queued, stop).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(calls, Is.EqualTo(1));
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Stopped));
    }

    [Test]
    public async Task RestoreDiscardsOldChoiceAndAllowsAFreshRun()
    {
        var choice = new PendingChoicePresenter();
        using var view = View(_ => new ImmediatePrimitiveInstance());
        await using var engine = new GameEngine(ChoiceGraph(), view, choicePresenter: choice);
        var snapshot = engine.CreateSaveData();
        await engine.AdvanceAsync();
        var oldResult = choice.Result;
        var oldToken = choice.Token;
        var completion = new TaskCompletionSource<GameEngineStateChange>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.StateChanged += change =>
        {
            if (change.State is GameEngineState.Completed or GameEngineState.Faulted) completion.TrySetResult(change);
        };

        await engine.RestoreFromAsync(snapshot);
        oldResult.SetException(new InvalidOperationException("obsolete"));
        await engine.AdvanceAsync();
        choice.Result.SetResult(0);
        var terminal = await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(oldToken.IsCancellationRequested, Is.True);
        Assert.That(engine.CurrentNodeId, Is.EqualTo("choice"));
        Assert.That(terminal.State, Is.EqualTo(GameEngineState.Completed));
        Assert.That(terminal.Error, Is.Null);
    }

    [Test]
    public async Task ObserversRunOutsideTheExecutionLockAndCannotFaultTheStory()
    {
        using var view = View(_ => new ImmediatePrimitiveInstance());
        await using var engine = new GameEngine(GroupGraph(), view);
        var snapshot = engine.CreateSaveData();
        Task? restore = null;
        engine.StateChanged += _ => throw new InvalidOperationException("observer failed");
        engine.StateChanged += change =>
        {
            if (change.State == GameEngineState.Completed) restore = engine.RestoreFromAsync(snapshot);
        };

        await engine.AdvanceAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await restore!.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(engine.State, Is.EqualTo(GameEngineState.Ready));
    }

    [Test]
    public async Task StopAndDisposeDoNotOwnTheExecutionView()
    {
        var module = new OwnedModule();
        await using var view = new GameRegistration()
            .RegisterSchema(new DefaultEntryModule("test", [new DefaultPrimitiveEntryBase("test.run", DynamicParameterTable.Empty)]))
            .RegisterImplementation(module).BuildView();
        var engine = new GameEngine(GroupGraph(), view);

        await engine.StopAsync();
        await engine.DisposeAsync();
        await engine.DisposeAsync();

        Assert.That(module.Disposals, Is.Zero);
        await view.DisposeAsync();
        Assert.That(module.Disposals, Is.EqualTo(1));
    }

    private static CompositeGameView View(Func<PrimitiveCreateContext, PrimitiveInstance> factory) =>
        new GameRegistration()
            .RegisterSchema(new DefaultEntryModule("test", [new DefaultPrimitiveEntryBase("test.run", DynamicParameterTable.Empty)]))
            .RegisterInstruction("test.run", factory).BuildView();

    [Test]
    public async Task StopWaitsForProgressFlush()
    {
        using var view = View(_ => new ImmediatePrimitiveInstance());
        var progress = new DelayedProgress();
        await using var engine = new GameEngine(GroupGraph(), view, progress: progress);
        var stop = engine.StopAsync();
        await progress.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(stop.IsCompleted, Is.False);
        progress.Release.TrySetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Stopped));
    }

    [Test]
    public void FlushFailureIsReportedAfterTheEngineReachesStopped()
    {
        using var view = View(_ => new ImmediatePrimitiveInstance());
        var progress = new DelayedProgress { Fail = true };
        progress.Release.TrySetResult();
        var engine = new GameEngine(GroupGraph(), view, progress: progress);
        Assert.ThrowsAsync<AggregateException>(() => engine.StopAsync());
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Stopped));
        Assert.ThrowsAsync<AggregateException>(async () => await engine.DisposeAsync());
    }

    private sealed class DelayedProgress : IGameProgressService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Fail { get; init; }
        public bool IsRead(string groupId, string entryId) => false;
        public void MarkRead(string groupId, string entryId) { }
        public async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            if (Fail) throw new IOException("Progress store failed.");
        }
    }

    private static Graph GroupGraph(int count = 1) => new()
    {
        RootNodeId = "main",
        Nodes = { new Group { Id = "main", Entries = Enumerable.Range(0, count)
            .Select(_ => new PrimitiveEntry("test.run", JsonSerializer.SerializeToElement(new { }))).Cast<Entry>().ToList() } }
    };

    private static Graph ChoiceGraph() => new()
    {
        RootNodeId = "choice",
        Nodes = { new Branch { Id = "choice", BranchType = BranchType.Choice, Options = { new BranchOption { Text = "Continue" } } } }
    };

    private sealed class Blocker : PrimitiveInstance
    {
        public override bool IsBlocking => true;
        public override bool IsSkippable => false;
        protected override void OnDispatch() { }
        public void Finish() => TryComplete();
    }

    private sealed class PendingChoicePresenter : IChoicePresenter
    {
        public TaskCompletionSource<int> Result { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public Task<int> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            Result = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            return Result.Task;
        }
    }

    private sealed class OwnedModule() : PrimitiveModule(registry => registry.Register("test.run", _ => new ImmediatePrimitiveInstance()))
    {
        public int Disposals { get; private set; }
        public override void Dispose() { Disposals++; base.Dispose(); }
    }
}
