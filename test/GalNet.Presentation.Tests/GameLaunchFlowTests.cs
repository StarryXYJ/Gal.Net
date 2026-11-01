using GalNet.Avalonia.GameView.Services;
using GalNet.Presentation.Abstractions.Application;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace GeneralTest.Presentation;

public sealed class GameLaunchFlowTests
{
    [TestCase("new", false)]
    [TestCase("continue", false)]
    [TestCase("load", false)]
    [TestCase("new", true)]
    [TestCase("continue", true)]
    [TestCase("load", true)]
    [TestCase("variables", true)]
    public async Task SessionActionsUseTheirCapabilityAndMountBeforePreparedPlayback(string action, bool prepared)
    {
        Session session = prepared ? new PreparedSession() : new Session();
        var flow = new GameLaunchFlow(_ => { session.Events.Add("loading"); return Task.CompletedTask; },
            _ => { session.Events.Add("game"); return Task.CompletedTask; },
            _ => throw new AssertionException("Unexpected failure route."));
        IReadOnlyDictionary<string, object> variables = new Dictionary<string, object> { ["route"] = "harbor" };
        await (action switch
        {
            "new" => flow.StartNewGameAsync(session),
            "continue" => flow.ContinueAsync(session),
            "load" => flow.LoadAsync(session, 7),
            _ => flow.StartNewGameAsync(session, variables)
        });
        var operation = action == "load" ? "load:7" : action;
        Assert.That(session.Events, Is.EqualTo(prepared
            ? new[] { "loading", "prepare:" + operation, "game", "begin" }
            : new[] { "loading", "legacy:" + operation, "game" }));
        if (session is PreparedSession configured && action == "variables") Assert.That(configured.Variables, Is.SameAs(variables));
    }

    [Test]
    public void UnsupportedVariablesFailBeforeChangingPages()
    {
        var flow = new GameLaunchFlow(_ => throw new AssertionException("Must validate before navigation."),
            _ => throw new AssertionException("Must not mount gameplay."), _ => throw new AssertionException("Must not recover a flow that never began."));
        Assert.Throws<NotSupportedException>(() => flow.StartNewGameAsync(new Session(), new Dictionary<string, object>()));
    }

    [Test]
    public async Task CancellationCannotBeginPlaybackWhenPreparationIgnoresItsToken()
    {
        using var cancellation = new CancellationTokenSource();
        var preparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovered = false;
        var flow = new GameLaunchFlow(_ => loading.Task,
            _ => throw new AssertionException("Canceled startup must not show gameplay."),
            _ => { recovered = true; return Task.CompletedTask; });
        var run = flow.RunAsync(async _ => { preparing.SetResult(); await prepared.Task; },
            _ => throw new AssertionException("Canceled startup must not begin."), cancellation.Token);
        await preparing.Task;
        cancellation.Cancel();
        loading.SetResult();
        Assert.That(run.IsCompleted, Is.False);
        prepared.SetResult();
        Assert.That(async () => await run, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(recovered, Is.True);
    }

    [Test]
    public async Task PreparationAndLoadingPresentationCompleteBeforePlaybackBegins()
    {
        var loading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shown = false;
        var began = false;
        var flow = new GameLaunchFlow(_ => loading.Task,
            _ => { shown = true; return Task.CompletedTask; },
            _ => throw new AssertionException("Successful startup must not show the failure page."));
        var run = flow.RunAsync(async _ => { preparing.SetResult(); await prepared.Task; },
            _ => { Assert.That(shown, Is.True); began = true; return Task.CompletedTask; });
        await preparing.Task;
        Assert.That(shown, Is.False);
        prepared.SetResult();
        Assert.That(began, Is.False);
        loading.SetResult();
        await run;
        Assert.That(began, Is.True);
    }

    [Test]
    public async Task FailedPresentationCancelsAndDrainsPreparationBeforeShowingFailure()
    {
        var loading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var drained = false;
        var recovered = false;
        var flow = new GameLaunchFlow(_ => loading.Task,
            _ => throw new AssertionException("Failed loading must not show gameplay."),
            _ => { Assert.That(drained, Is.True); recovered = true; return Task.CompletedTask; });
        var run = flow.RunAsync(async token =>
        {
            preparing.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally
            {
                cleanupStarted.SetResult();
                await releaseCleanup.Task;
                drained = true;
            }
        }, null);
        await preparing.Task;
        loading.SetException(new IOException("presentation failed"));
        await cleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { Assert.That(run.IsCompleted, Is.False, "The host must not finish while preparation cleanup is pending."); }
        finally { releaseCleanup.TrySetResult(); }
        try { await run; Assert.Fail("The startup failure must propagate."); }
        catch (IOException error) { Assert.That(error.Message, Is.EqualTo("presentation failed")); }
        Assert.That(recovered, Is.True);
    }

    private class Session : IGameSessionService
    {
        public List<string> Events { get; } = [];
        public string GameTitle => "Test";
        public string StatusMessage => "";
        public bool IsReady => true;
        public bool IsPlaying => false;
        public bool CanContinue => true;
        public ReadOnlyObservableCollection<GameSaveSlot> SaveSlots { get; } = new(new ObservableCollection<GameSaveSlot>());
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        protected Task Record(string value) { Events.Add(value); return Task.CompletedTask; }
        public Task StartNewGameAsync(CancellationToken cancellationToken = default) => Record("legacy:new");
        public Task ContinueAsync(CancellationToken cancellationToken = default) => Record("legacy:continue");
        public Task LoadAsync(int slotIndex, CancellationToken cancellationToken = default) => Record("legacy:load:" + slotIndex);
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(int slotIndex, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class PreparedSession : Session, IParameterizedGameSessionService
    {
        public IReadOnlyDictionary<string, object>? Variables { get; private set; }
        public Task PrepareNewGameAsync(CancellationToken cancellationToken = default) => Record("prepare:new");
        public Task PrepareContinueAsync(CancellationToken cancellationToken = default) => Record("prepare:continue");
        public Task PrepareLoadAsync(int slotIndex, CancellationToken cancellationToken = default) => Record("prepare:load:" + slotIndex);
        public Task BeginPreparedGameAsync(CancellationToken cancellationToken = default) => Record("begin");
        public Task StartNewGameAsync(IReadOnlyDictionary<string, object> variables, CancellationToken cancellationToken = default) => throw new AssertionException("Expected preparation.");
        public Task PrepareNewGameAsync(IReadOnlyDictionary<string, object> variables, CancellationToken cancellationToken = default)
        { Variables = variables; return Record("prepare:variables"); }
    }
}
