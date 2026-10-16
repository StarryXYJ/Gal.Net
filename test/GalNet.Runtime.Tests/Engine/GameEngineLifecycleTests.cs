using System.Collections.Concurrent;
using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Progress;

namespace GeneralTest.Runtime;

public sealed class GameEngineLifecycleTests
{
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
        Assert.Throws<InvalidOperationException>(() => engine.RestoreFrom(engine.CreateSaveData()));
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

        engine.RestoreFrom(snapshot);
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
        engine.StateChanged += _ => throw new InvalidOperationException("observer failed");
        engine.StateChanged += change =>
        {
            if (change.State == GameEngineState.Completed) engine.RestoreFrom(snapshot);
        };

        await engine.AdvanceAsync().WaitAsync(TimeSpan.FromSeconds(2));

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
