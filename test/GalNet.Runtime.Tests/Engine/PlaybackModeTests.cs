using System.Collections.Concurrent;
using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Core.Settings;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Progress;

namespace GeneralTest.Runtime;

public class PlaybackModeTests
{
    [TestCase(PlaybackMode.Auto)]
    [TestCase(PlaybackMode.Quick)]
    public async Task CancelledCatchUpOnExistingUnskippableBlockerPreservesPlaybackBoundary(PlaybackMode mode)
    {
        var blocker = new UnskippableWait();
        using var view = Registration().RegisterInstruction("test.voice", _ => blocker).BuildView();
        await using var engine = new GameEngine(GraphOf("test.voice"), view, settings: Settings(0));
        var completed = Terminal(engine);
        await engine.AdvanceAsync();
        var skip = engine.SkipToUnreadAsync();
        await blocker.CatchUpEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        engine.CancelCatchUp();
        await skip.WaitAsync(TimeSpan.FromSeconds(2));
        await engine.SetPlaybackModeAsync(mode);
        Assert.That(blocker.IsCompleted, Is.False);
        blocker.Finish();
        await completed.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestCase(PlaybackMode.Auto)]
    [TestCase(PlaybackMode.Quick)]
    public async Task ManualRevealPreservesBoundaryForPlayback(PlaybackMode mode)
    {
        var line = new ReadingWait(skipCompletes: false);
        var settings = Settings(0);
        settings.Get<GameSettings>().QuickReadOnly = false;
        using var view = Registration().RegisterInstruction("test.text", _ => line).BuildView();
        await using var engine = new GameEngine(GraphOf("test.text"), view, settings: settings);
        var completed = Terminal(engine);
        await engine.AdvanceAsync();
        await engine.AdvanceAsync();
        Assert.That(line.IsWaitingForAdvance, Is.True);
        await engine.SetPlaybackModeAsync(mode);
        await completed.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(line.IsCompleted, Is.True);
    }

    [TestCase(PlaybackMode.Auto)]
    [TestCase(PlaybackMode.Quick)]
    public async Task ManualClickOnUnskippableBlockerPreservesBoundaryForPlayback(PlaybackMode mode)
    {
        var blocker = new UnskippableWait();
        using var view = Registration().RegisterInstruction("test.voice", _ => blocker).BuildView();
        await using var engine = new GameEngine(GraphOf("test.voice"), view, settings: Settings(0));
        var completed = Terminal(engine);
        await engine.AdvanceAsync();
        await engine.AdvanceAsync();
        await engine.SetPlaybackModeAsync(mode);
        Assert.That(blocker.IsCompleted, Is.False);
        Assert.That(engine.IsRunning, Is.True);
        blocker.Finish();
        await completed.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task PresentationSynchronizationUsesAnIsolatedRunScopeThatRestoreAndStopCancel()
    {
        using var view = Registration().RegisterInstruction("test.text", _ => new ReadingWait()).BuildView();
        await using var engine = new GameEngine(GraphOf("test.text"), view);
        IGameContext? oldRuntime = null;
        CancellationToken oldToken = default;
        await engine.SynchronizePresentationAsync((runtime, token) =>
        {
            oldRuntime = runtime;
            oldToken = token;
            return Task.CompletedTask;
        });
        Assert.That(oldToken.CanBeCanceled, Is.True);
        await engine.RestoreFromAsync(engine.Runtime.CreateSnapshot());
        Assert.That(oldToken.IsCancellationRequested, Is.True);
        Assert.That(oldRuntime!.State, Is.Not.SameAs(engine.Runtime.State));
        Assert.Throws<OperationCanceledException>(() => oldRuntime.SetVariable("late", 1));
        CancellationToken currentToken = default;
        await engine.SynchronizePresentationAsync((_, token) => { currentToken = token; return Task.CompletedTask; });
        await engine.StopAsync();
        Assert.That(currentToken.IsCancellationRequested, Is.True);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await engine.SynchronizePresentationAsync((_, _) => Task.CompletedTask));
    }

    [Test]
    public async Task PresentationFailureIsReportedButDoesNotPreventStopCleanup()
    {
        var progress = new MemoryProgress();
        progress.MarkRead("main", "line-0");
        var first = new ReadingWait();
        var next = new ReadingWait();
        var port = new ControlledCatchUpPresentation();
        using var view = Registration().RegisterInstruction("test.text", context => context.Entry.StableId == "line-0" ? first : next).BuildView();
        await using var engine = new GameEngine(GraphOf("test.text", "test.text"), view, progress: progress, catchUpPresentation: port);
        await engine.AdvanceAsync();
        first.Presented();
        var catchUp = engine.SkipToUnreadAsync();
        await port.EndEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        port.EndReleased.SetException(new InvalidOperationException("final scene failed"));
        var error = Assert.ThrowsAsync<InvalidOperationException>(async () => await catchUp);
        Assert.That(error!.Message, Is.EqualTo("final scene failed"));
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Faulted));
        Assert.That(next.HasBeenDispatched, Is.False);
        await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Stopped));
    }

    [Test]
    public async Task CatchUpLoopYieldsAndCanBeCancelledWithoutEndingTheRun()
    {
        var progress = new MemoryProgress();
        progress.MarkRead("main", "line-0");
        var first = new ReadingWait();
        var iterations = 0;
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var view = Registration().RegisterInstruction("test.text", _ => first)
            .RegisterInstruction("test.voice", _ => new ImmediatePrimitiveInstance(() =>
            {
                if (Interlocked.Increment(ref iterations) == 1000) reached.SetResult();
            })).BuildView();
        var graph = GraphOf("test.text");
        graph.Nodes.Add(new Group { Id = "loop", Entries = { new PrimitiveEntry("test.voice", JsonSerializer.SerializeToElement(new { })) } });
        graph.Edges.AddRange([new Edge("main", 0, "loop"), new Edge("loop", 0, "loop")]);
        await using var engine = new GameEngine(graph, view, progress: progress);
        await engine.AdvanceAsync();
        first.Presented();
        using var cancellation = new CancellationTokenSource();
        var catchUp = engine.SkipToUnreadAsync(cancellation.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await catchUp.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(engine.IsCatchingUp, Is.False);
        Assert.That(engine.IsRunning, Is.True);
        Assert.That(engine.PlaybackMode, Is.EqualTo(PlaybackMode.Manual));
    }

    [Test]
    public async Task CatchUpUsesUpdatedVariablesAndStopsAtChoiceWithoutSelectingIt()
    {
        var progress = new MemoryProgress();
        progress.MarkRead("main", "line-0");
        var first = new ReadingWait();
        var choice = new PendingChoice();
        using var view = Registration().RegisterInstruction("test.text", _ => first)
            .RegisterInstruction("test.voice", context => new ImmediatePrimitiveInstance(() => context.Runtime.SetVariable("route", true))).BuildView();
        var graph = GraphOf("test.text", "test.voice");
        graph.Nodes.Add(new Branch
        {
            Id = "condition",
            BranchType = BranchType.Condition,
            Conditions = { new BranchCondition { Expression = "route == true" }, new BranchCondition { Expression = "true" } }
        });
        graph.Nodes.Add(new Branch
        {
            Id = "choice",
            BranchType = BranchType.Choice,
            Options = { new BranchOption { Text = "Choose", Condition = "route == true" } }
        });
        graph.Nodes.Add(new Group { Id = "wrong" });
        graph.Edges.AddRange([new Edge("main", 0, "condition"), new Edge("condition", 0, "choice"), new Edge("condition", 1, "wrong")]);
        await using var engine = new GameEngine(graph, view, progress: progress, choicePresenter: choice);
        await engine.AdvanceAsync();
        first.Presented();
        await engine.SkipToUnreadAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(engine.CurrentNodeId, Is.EqualTo("choice"));
        Assert.That(choice.Calls, Is.EqualTo(1));
        Assert.That(choice.Options, Is.EqualTo(new[] { "Choose" }));
        Assert.That(engine.IsRunning, Is.True);
        Assert.That(engine.PlaybackMode, Is.EqualTo(PlaybackMode.Manual));
        await engine.SetPlaybackModeAsync(PlaybackMode.Auto);
        Assert.That(engine.PlaybackMode, Is.EqualTo(PlaybackMode.Manual));
    }

    [Test]
    public async Task ModeSwitchWaitsForCatchUpPresentationCleanup()
    {
        var progress = new MemoryProgress();
        progress.MarkRead("main", "line-0");
        var line = new ReadingWait();
        var blocker = new UnskippableWait();
        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var port = new ControlledCatchUpPresentation();
        using var view = Registration().RegisterInstruction("test.text", _ => line)
            .RegisterInstruction("test.voice", _ => { dispatched.SetResult(); return blocker; }).BuildView();
        await using var engine = new GameEngine(GraphOf("test.text", "test.voice"), view,
            progress: progress, catchUpPresentation: port);
        await engine.AdvanceAsync();
        line.Presented();
        var catchUp = engine.SkipToUnreadAsync();
        await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var switchMode = engine.SetPlaybackModeAsync(PlaybackMode.Auto);
        await port.EndEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(switchMode.IsCompleted, Is.False);
        port.EndReleased.SetResult();
        await Task.WhenAll(catchUp, switchMode).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(engine.PlaybackMode, Is.EqualTo(PlaybackMode.Auto));
        Assert.That(blocker.InvocationMode, Is.EqualTo(PrimitiveInvocationMode.Normal));
    }

    [Test]
    public async Task FinalSceneIsSynchronizedBeforeUnreadTargetDispatch()
    {
        var progress = new MemoryProgress();
        progress.MarkRead("main", "line-0");
        var first = new ReadingWait();
        var next = new ReadingWait();
        var port = new ControlledCatchUpPresentation();
        using var view = Registration().RegisterInstruction("test.text", context => context.Entry.StableId == "line-0" ? first : next)
            .RegisterInstruction("test.voice", _ => new ImmediatePrimitiveInstance(() =>
                Assert.That(port.EndReleased.Task.IsCompleted, Is.True))).BuildView();
        var graph = GraphOf("test.text", "test.voice", "test.text");
        ((PrimitiveEntry)((Group)graph.Nodes[0]).Entries[1]).ReadingBoundaryId = "line-2";
        await using var engine = new GameEngine(graph, view, progress: progress, catchUpPresentation: port);
        await engine.AdvanceAsync();
        first.Presented();
        var catchUp = engine.SkipToUnreadAsync();
        await port.EndEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(next.HasBeenDispatched, Is.False);
        port.EndReleased.SetResult();
        await catchUp.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(next.HasBeenDispatched, Is.True);
        Assert.That(next.InvocationMode, Is.EqualTo(PrimitiveInvocationMode.Normal));
    }

    [Test]
    public async Task RestoreInvalidatesOldTimerAndOldCompletion()
    {
        var first = new ReadingWait();
        var next = new ReadingWait();
        var calls = 0;
        using var view = Registration().RegisterInstruction("test.text", _ => ++calls == 1 ? first : next).BuildView();
        await using var engine = new GameEngine(GraphOf("test.text"), view, settings: Settings(50));
        var snapshot = engine.CreateSaveData();
        await engine.AdvanceAsync();
        first.Presented();
        await engine.SetPlaybackModeAsync(PlaybackMode.Auto);
        await engine.RestoreFromAsync(snapshot);
        await engine.AdvanceAsync();
        first.CompleteAdvance();
        await Task.Delay(100);
        Assert.That(next.IsCompleted, Is.False);
        Assert.That(engine.PlaybackMode, Is.EqualTo(PlaybackMode.Manual));
        Assert.That(engine.IsRunning, Is.True);
    }

    [Test]
    public async Task StopCancelsCatchUpWaitingForAnUnskippablePrimitive()
    {
        var progress = new MemoryProgress();
        progress.MarkRead("main", "line-0");
        var first = new ReadingWait();
        var blocker = new UnskippableWait();
        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var view = Registration().RegisterInstruction("test.text", _ => first)
            .RegisterInstruction("test.voice", _ => { dispatched.SetResult(); return blocker; }).BuildView();
        await using var engine = new GameEngine(GraphOf("test.text", "test.voice"), view, progress: progress);
        await engine.AdvanceAsync();
        first.Presented();
        var catchUp = engine.SkipToUnreadAsync();
        await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await catchUp;
        blocker.Finish();
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Stopped));
        Assert.That(engine.IsCatchingUp, Is.False);
    }

    [Test]
    public async Task CatchUpExecutesRealPathAndStopsBeforeNextUnreadVoice()
    {
        var progress = new MemoryProgress();
        progress.MarkRead("main", "line-0");
        var first = new ReadingWait();
        var next = new ReadingWait();
        var effects = 0;
        using var view = Registration().RegisterInstruction("test.text", context => context.Entry.StableId == "line-0" ? first : next)
            .RegisterInstruction("test.voice", _ => new ImmediatePrimitiveInstance(() => effects++)).BuildView();
        var graph = GraphOf("test.text", "test.voice", "test.voice", "test.text");
        ((PrimitiveEntry)((Group)graph.Nodes[0]).Entries[2]).ReadingBoundaryId = "line-3";
        await using var engine = new GameEngine(graph, view, progress: progress);
        await engine.AdvanceAsync();
        first.Presented();
        await engine.SkipToUnreadAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Multiple(() =>
        {
            Assert.That(effects, Is.EqualTo(2), "Both the passed command and target voice execute exactly once.");
            Assert.That(next.InvocationMode, Is.EqualTo(PrimitiveInvocationMode.Normal));
            Assert.That(next.IsCompleted, Is.False);
            Assert.That(engine.IsCatchingUp, Is.False);
        });
    }

    [Test]
    public async Task CatchUpDoesNotCrossCurrentUnreadText()
    {
        var line = new ReadingWait();
        using var view = Registration().RegisterInstruction("test.text", _ => line).BuildView();
        await using var engine = new GameEngine(GraphOf("test.text"), view);
        await engine.AdvanceAsync();
        line.Presented();
        await engine.SkipToUnreadAsync();
        Assert.That(line.IsCompleted, Is.False);
    }

    [Test]
    public async Task AutoWaitsForEveryDeclaredBlockerWithoutCompletingTextEarly()
    {
        var voice = new AutoBlocker();
        var line = new ReadingWait();
        using var view = Registration().RegisterInstruction("test.voice", _ => voice)
            .RegisterInstruction("test.text", _ => line).BuildView();
        await using var engine = new GameEngine(GraphOf("test.voice", "test.text"), view, settings: Settings(0));
        var completed = Terminal(engine);
        await engine.AdvanceAsync();
        await engine.SetPlaybackModeAsync(PlaybackMode.Auto);
        line.Presented();
        await Task.Delay(30);
        Assert.That(line.IsCompleted, Is.False);
        voice.Finish();
        await completed.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(line.IsCompleted, Is.True);
        Assert.That(engine.PlaybackMode, Is.EqualTo(PlaybackMode.Manual));
    }

    [Test]
    public async Task NewlyAppearingBlockerCancelsPendingInterval()
    {
        var voice = new AutoBlocker();
        var line = new ReadingWait();
        using var view = Registration().RegisterInstruction("test.voice", _ => voice)
            .RegisterInstruction("test.text", _ => line).BuildView();
        await using var engine = new GameEngine(GraphOf("test.voice", "test.text"), view, settings: Settings(80));
        var completed = Terminal(engine);
        await engine.AdvanceAsync();
        voice.Block(false);
        line.Presented();
        await engine.SetPlaybackModeAsync(PlaybackMode.Auto);
        voice.Block(true);
        await Task.Delay(120);
        Assert.That(line.IsCompleted, Is.False);
        voice.Finish();
        await completed.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task ManualCancellationInvalidatesAnAlreadyScheduledTimer()
    {
        var line = new ReadingWait();
        using var view = Registration().RegisterInstruction("test.text", _ => line).BuildView();
        await using var engine = new GameEngine(GraphOf("test.text"), view, settings: Settings(50));
        await engine.AdvanceAsync();
        line.Presented();
        await engine.SetPlaybackModeAsync(PlaybackMode.Auto);
        await engine.SetPlaybackModeAsync(PlaybackMode.Manual);
        await Task.Delay(100);
        Assert.That(line.IsCompleted, Is.False);
        Assert.That(engine.IsRunning, Is.True);
    }

    [Test]
    public async Task CurrentUnreadTextCannotBecomeQuickEligibleByItsOwnReadWrite()
    {
        var line = new ReadingWait();
        var progress = new MemoryProgress();
        using var view = Registration().RegisterInstruction("test.text", _ => line).BuildView();
        await using var engine = new GameEngine(GraphOf("test.text"), view, progress: progress);
        await engine.AdvanceAsync();
        Assert.That(progress.IsRead("main", "line-0"), Is.False);
        line.Presented();
        await engine.SetPlaybackModeAsync(PlaybackMode.Quick);
        Assert.That(progress.IsRead("main", "line-0"), Is.True);
        Assert.That(engine.PlaybackMode, Is.EqualTo(PlaybackMode.Manual));
    }

    [Test]
    public async Task QuickStopsBeforeVoiceOfNextUnreadComposite()
    {
        var progress = new MemoryProgress();
        progress.MarkRead("main", "line-0");
        var first = new ReadingWait();
        var next = new ReadingWait();
        PrimitiveInvocationMode? voiceMode = null;
        using var view = Registration().RegisterInstruction("test.text", context => context.Entry.StableId == "line-0" ? first : next)
            .RegisterInstruction("test.voice", _ => new RecordMode(mode => voiceMode = mode)).BuildView();
        var graph = GraphOf("test.text", "test.voice", "test.text");
        ((PrimitiveEntry)((Group)graph.Nodes[0]).Entries[1]).ReadingBoundaryId = "line-2";
        await using var engine = new GameEngine(graph, view, settings: Settings(0), progress: progress);
        await engine.AdvanceAsync();
        await engine.SetPlaybackModeAsync(PlaybackMode.Quick);
        first.Presented();
        for (var attempt = 0; voiceMode is null && attempt < 100; attempt++) await Task.Delay(5);
        Assert.That(voiceMode, Is.EqualTo(PrimitiveInvocationMode.Normal));
        Assert.That(engine.PlaybackMode, Is.EqualTo(PlaybackMode.Manual));
        Assert.That(next.IsCompleted, Is.False);
    }

    private static GameRegistration Registration() => new GameRegistration().RegisterSchema(new DefaultEntryModule("test",
        [new DefaultPrimitiveEntryBase("test.text", DynamicParameterTable.Empty) { IsReading = true },
         new DefaultPrimitiveEntryBase("test.voice", DynamicParameterTable.Empty)]));

    [Test]
    public async Task CatchUpDrainsOldPresentationBeforeDispatchingTheNextReadingBoundary()
    {
        var old = new DelayedPresentationCleanup();
        var current = new ReadingWait();
        var next = new ReadingWait();
        var progress = new MemoryProgress();
        progress.MarkRead("main", "line-1");
        using var view = Registration().RegisterInstruction("test.voice", _ => old)
            .RegisterInstruction("test.text", context => context.Entry.StableId == "line-1" ? current : next).BuildView();
        await using var engine = new GameEngine(GraphOf("test.voice", "test.text", "test.text"), view, progress: progress);
        await engine.AdvanceAsync();
        current.Presented();
        var skip = engine.SkipToUnreadAsync();
        await old.Detached.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(old.IsCompleted, Is.True);
        Assert.That(skip.IsCompleted, Is.False);
        Assert.That(next.HasBeenDispatched, Is.False);
        old.Cleaned.TrySetResult();
        await skip.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(next.HasBeenDispatched, Is.True);
    }

    private sealed class DelayedPresentationCleanup : PrimitiveInstance
    {
        public TaskCompletionSource Detached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cleaned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task PresentationCompletion => Cleaned.Task;
        public override bool IsBlocking => false;
        public override bool IsSkippable => false;
        protected override void OnDispatch() { }
        protected override void OnPlaybackModeChanged(PrimitiveInvocationMode mode)
        {
            if (mode != PrimitiveInvocationMode.CatchUp) return;
            TryComplete();
            Detached.TrySetResult();
        }
    }

    private static Graph GraphOf(params string[] types) => new()
    {
        RootNodeId = "main",
        Nodes = { new Group { Id = "main", Entries = types.Select((type, index) => (Entry)new PrimitiveEntry(type,
            JsonSerializer.SerializeToElement(new { })) { StableId = $"line-{index}" }).ToList() } }
    };

    private static SettingsContainer Settings(int interval)
    {
        var settings = new SettingsContainer();
        settings.Set(new GameSettings { AutoAdvanceIntervalMilliseconds = interval, QuickAdvanceIntervalMilliseconds = interval });
        return settings;
    }

    private static Task Terminal(GameEngine engine)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.StateChanged += change =>
        {
            if (change.State == GameEngineState.Completed) completed.TrySetResult();
            else if (change.Error is { } error) completed.TrySetException(error);
        };
        return completed.Task;
    }

    private sealed class MemoryProgress : IGameProgressService
    {
        private readonly ConcurrentDictionary<(string, string), bool> _read = new();
        public bool IsRead(string groupId, string entryId) => _read.ContainsKey((groupId, entryId));
        public void MarkRead(string groupId, string entryId) => _read.TryAdd((groupId, entryId), true);
    }

    private sealed class AutoBlocker : PrimitiveInstance
    {
        public override bool IsBlocking => false;
        public override bool IsSkippable => false;
        protected override void OnDispatch() => SetAutoBlocking(true);
        public void Block(bool value) => SetAutoBlocking(value);
        public void Finish() => TryComplete();
    }

    private sealed class UnskippableWait : PrimitiveInstance
    {
        public TaskCompletionSource CatchUpEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool IsBlocking => true;
        public override bool IsSkippable => false;
        protected override void OnDispatch() { }
        protected override void OnPlaybackModeChanged(PrimitiveInvocationMode mode)
        {
            if (mode == PrimitiveInvocationMode.CatchUp) CatchUpEntered.TrySetResult();
        }
        public void Finish() => TryComplete();
    }

    private sealed class ReadingWait(bool skipCompletes = true) : PrimitiveInstance, IAdvanceWaitInstance
    {
        public override bool IsBlocking => true;
        public override bool IsSkippable => true;
        public bool IsWaitingForAdvance => IsTextPresented && !IsCompleted;
        public bool IsTextPresented { get; private set; }
        public bool HasBeenDispatched { get; private set; }
        protected override void OnDispatch() => HasBeenDispatched = true;
        public void Presented() { IsTextPresented = true; SetAutoBlocking(false); }
        public void CompleteAdvance() => TryComplete();
        protected override void OnSkip()
        {
            if (skipCompletes) CompleteAdvance();
            else Presented();
        }
        protected override void OnPlaybackModeChanged(PrimitiveInvocationMode mode)
        {
            if (mode == PrimitiveInvocationMode.CatchUp) CompleteAdvance();
        }
    }

    private sealed class ControlledCatchUpPresentation : ICatchUpPresentation
    {
        public TaskCompletionSource EndEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource EndReleased { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task BeginCatchUpAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EndCatchUpAsync(IGameContext runtime, CancellationToken cancellationToken)
        {
            EndEntered.TrySetResult();
            return EndReleased.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class PendingChoice : IChoicePresenter
    {
        public int Calls { get; private set; }
        public IReadOnlyList<string> Options { get; private set; } = [];
        public Task<int> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellationToken)
        {
            Calls++;
            Options = options;
            return new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }
    }

    private sealed class RecordMode(Action<PrimitiveInvocationMode> record) : PrimitiveInstance
    {
        public override bool IsBlocking => false;
        public override bool IsSkippable => false;
        protected override void OnDispatch() { record(InvocationMode); TryComplete(); }
    }
}
