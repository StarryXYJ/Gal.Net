using System.Text.Json;
using GalNet.Avalonia.GameHost;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.I18n;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.GameHost;
using GalNet.Presentation.Abstractions.Application;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Progress;
using GalNet.Runtime.Runtime;
using GalNet.Storage.FileSystem;

namespace GeneralTest.Runtime;

public sealed class DialogueHistoryTests
{
    [Test]
    public async Task HostCanOpenAnOlderSaveWithoutInventingHistoricalTextOrRelaxingOtherStateValidation()
    {
        using var registration = new GameRegistration();
        registration.AddBuiltins(dialoguePresenter: new ImmediateText());
        registration.RegisterImplementation(DialogueHistoryJournal.CreateModule());
        await using var view = registration.BuildView();
        var runtime = new GameRuntime(null, "main", stateCatalog: view.StateCatalog);
        var older = runtime.CreateSnapshot();
        older.States.Remove(DialogueHistoryJournal.StateId);
        var prepared = DialogueHistoryJournal.PrepareSnapshot(older);
        Assert.That(older.States.ContainsKey(DialogueHistoryJournal.StateId), Is.False);
        var restored = GameState.Restore(view.StateCatalog, prepared.States);
        Assert.That(restored.Table<DialogueHistoryCursor>().Values, Is.Empty);
        var journal = new DialogueHistoryJournal();
        journal.Restore(prepared);
        Assert.That(journal.Lines, Is.Empty);
        prepared.States.Remove("builtin.audio.tracks");
        Assert.That(() => GameState.Restore(view.StateCatalog, prepared.States), Throws.TypeOf<InvalidDataException>());
    }
    [Test]
    public async Task CheckpointFlushesPresentedTextEvenIfItsPresentationContinuationHasNotRun()
    {
        var journal = new DialogueHistoryJournal();
        var reading = new DelayedCompletionReading();
        using var registration = new GameRegistration();
        registration.AddBuiltinSchemas().RegisterInstruction(BuiltinEntryIds.Text, _ => reading);
        registration.RegisterImplementation(DialogueHistoryJournal.CreateModule());
        await using var view = registration.BuildView();
        var runtime = new GameRuntime(null, "main", stateCatalog: view.StateCatalog);
        var progress = new Progress();
        await using var history = new DialogueHistoryDispatcher(view, journal, progress, CharacterCatalog.Empty);
        var group = new Group { Id = "main" };
        group.Entries.Add(Text("line#1"));
        await using var engine = new GameEngine(new Graph { RootNodeId = "main", Nodes = { group } }, runtime, history, progress);
        await engine.AdvanceAsync();
        reading.IsTextPresented = true;
        await engine.AdvanceAsync();
        var saved = journal.Attach(engine.CreateSaveData());
        var restored = new DialogueHistoryJournal();
        restored.Restore(saved);
        Assert.That(restored.Lines.Count, Is.EqualTo(1), "A delayed observation must not leave the checkpoint cursor behind its entry position.");
        reading.FinishPresentation();
    }
    [Test]
    public async Task EngineCheckpointRestartsTheWholeVoicedLineAndKeepsHistoryWithoutDuplicates()
    {
        var journal = new DialogueHistoryJournal();
        using var registration = new GameRegistration();
        registration.AddBuiltins(dialoguePresenter: new ImmediateText(), audioConfiguration: BuiltinAudioDefaults.Create());
        registration.RegisterImplementation(DialogueHistoryJournal.CreateModule());
        await using var view = registration.BuildView();
        var runtime = new GameRuntime(null, "main", stateCatalog: view.StateCatalog);
        var progress = new Progress();
        await using var history = new DialogueHistoryDispatcher(view, journal, progress, CharacterCatalog.Empty);
        var group = new Group { Id = "main" };
        group.Entries.AddRange([Audio("one#1", "one#2"), Text("one#2"), Text("two#1")]);
        var graph = new Graph { RootNodeId = "main", Nodes = { group } };
        await using var restorer = new StateRestoreSession(view);
        await using var run = new GameRun(graph, runtime, history, progress, restoration: restorer);
        await run.PrepareAsync(null);
        await run.StartPreparedAsync();
        history.FlushCompleted();
        Assert.That(journal.Lines.Count, Is.EqualTo(1));
        var saved = journal.Attach(run.CreateSaveData());
        Assert.That(saved.EntryIndex, Is.Zero, "Saving a voiced line must not resume between its audio and text primitives.");
        journal.Restore(saved);
        await run.PrepareAsync(saved);
        await run.StartPreparedAsync();
        history.FlushCompleted();
        Assert.That(journal.Lines.Count, Is.EqualTo(1));
        Assert.That(journal.Lines[0].Voice, Is.Not.Null);
        await run.Engine.AdvanceAsync();
        history.FlushCompleted();
        Assert.That(journal.Lines.Count, Is.EqualTo(2));
        var secondSave = journal.Attach(run.CreateSaveData());
        var restored = new DialogueHistoryJournal();
        restored.Restore(secondSave);
        Assert.That(restored.Lines.Count, Is.EqualTo(1), "The currently displayed second line is replayed after restore.");
    }

    [Test]
    public async Task SkippedInstructionInvalidatesAnUnconsumedAudioCandidate()
    {
        var journal = new DialogueHistoryJournal();
        using var registration = new GameRegistration();
        registration.AddBuiltins(dialoguePresenter: new ImmediateText(), audioConfiguration: BuiltinAudioDefaults.Create());
        registration.RegisterImplementation(DialogueHistoryJournal.CreateModule());
        await using var view = registration.BuildView();
        var runtime = new GameRuntime(null, "main", stateCatalog: view.StateCatalog);
        await using var history = new DialogueHistoryDispatcher(view, journal, new Progress(), CharacterCatalog.Empty);
        history.Dispatch(Audio("line#1", "line#2"), runtime, default);
        history.OnInstructionSkipped(Text("line#2"), runtime);
        var text = history.Dispatch(Text("line#2"), runtime, default);
        await text.PresentationCompletion;
        history.FlushCompleted();
        Assert.That(journal.Lines.Single().Voice, Is.Null);
    }

    [Test]
    public async Task AdjacentSameSourceAudioDoesNotNeedToBeTheFirstPrimitiveOfAComposite()
    {
        var journal = new DialogueHistoryJournal();
        using var registration = new GameRegistration();
        registration.AddBuiltins(dialoguePresenter: new ImmediateText(), audioConfiguration: BuiltinAudioDefaults.Create());
        registration.RegisterImplementation(DialogueHistoryJournal.CreateModule());
        await using var view = registration.BuildView();
        var runtime = new GameRuntime(null, "main", stateCatalog: view.StateCatalog);
        await using var history = new DialogueHistoryDispatcher(view, journal, new Progress(), CharacterCatalog.Empty);
        var audio = Audio("line#2", "line#3");
        audio.ReadingBoundaryId = null;
        history.Dispatch(audio, runtime, default);
        var text = history.Dispatch(Text("line#3"), runtime, default);
        await text.PresentationCompletion;
        history.FlushCompleted();
        Assert.That(journal.Lines.Single().Voice?.AssetId, Is.EqualTo("voice"));
    }

    [TestCase(PrimitiveInvocationMode.Normal)]
    [TestCase(PrimitiveInvocationMode.Quick)]
    [TestCase(PrimitiveInvocationMode.CatchUp)]
    public async Task SameSourceVoiceIsKeptForHistoryEvenWhenAlreadyFinished(PrimitiveInvocationMode mode)
    {
        var journal = new DialogueHistoryJournal();
        using var registration = new GameRegistration();
        registration.AddBuiltins(dialoguePresenter: new ImmediateText(), audioConfiguration: BuiltinAudioDefaults.Create());
        registration.RegisterImplementation(DialogueHistoryJournal.CreateModule());
        await using var view = registration.BuildView();
        var runtime = new GameRuntime(null, "main", stateCatalog: view.StateCatalog);
        await using var history = new DialogueHistoryDispatcher(view, journal, new Progress(), CharacterCatalog.Empty);
        var audio = Audio("line#1", "line#2");
        history.Dispatch(audio, runtime, default, mode);
        var text = history.Dispatch(Text("line#2"), runtime, default, mode);
        await text.PresentationCompletion;
        history.FlushCompleted();
        Assert.That(journal.Lines.Single().Voice, Is.EqualTo(new DialogueVoice("voice", "vocal", .7f)));
        Assert.That(journal.Lines.Single().WasRead, Is.False);
        text.Skip();

        // A second visit to the same text cannot borrow the old voice association.
        var next = history.Dispatch(Text("line#2"), runtime, default, mode);
        await next.PresentationCompletion;
        history.FlushCompleted();
        Assert.That(journal.Lines.Count, Is.EqualTo(2));
        Assert.That(journal.Lines[1].Voice, Is.Null);
    }

    [Test]
    public async Task DifferentSourceAndDebuggerReconstructionDoNotManufactureVoiceOrHistory()
    {
        var journal = new DialogueHistoryJournal();
        using var registration = new GameRegistration();
        registration.AddBuiltins(dialoguePresenter: new ImmediateText(), audioConfiguration: BuiltinAudioDefaults.Create());
        registration.RegisterImplementation(DialogueHistoryJournal.CreateModule());
        await using var view = registration.BuildView();
        var runtime = new GameRuntime(null, "main", stateCatalog: view.StateCatalog);
        await using var history = new DialogueHistoryDispatcher(view, journal, new Progress(), CharacterCatalog.Empty);
        history.Dispatch(Audio("other#1", "other#2"), runtime, default);
        var text = history.Dispatch(Text("line#1"), runtime, default);
        await text.PresentationCompletion;
        history.FlushCompleted();
        Assert.That(journal.Lines.Single().Voice, Is.Null);
        history.SuppressRecording = () => true;
        history.Dispatch(Text("line#1"), runtime, default, PrimitiveInvocationMode.CatchUp);
        Assert.That(journal.Lines.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task SaveAndQuickSaveCarryOnlyTheHistoryPrefixOfTheirCheckpoint()
    {
        var journal = new DialogueHistoryJournal();
        using var registration = new GameRegistration();
        registration.AddBuiltins(dialoguePresenter: new ImmediateText());
        registration.RegisterImplementation(DialogueHistoryJournal.CreateModule());
        await using var view = registration.BuildView();
        var runtime = new GameRuntime(null, "main", stateCatalog: view.StateCatalog);
        var first = new DialogueHistoryLine("main", "one#1", "Alice", "<b>Hello</b>", false, new("voice", "vocal"));
        journal.Append(runtime.State, first);
        var checkpoint = runtime.CreateSnapshot();
        journal.Append(runtime.State, first with { Text = "future branch" });
        var attached = journal.Attach(checkpoint);
        Assert.That(checkpoint.Attachments, Is.Empty, "Attaching history must not grow the live checkpoint.");
        var root = Path.Combine(Path.GetTempPath(), "galnet-history-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var saves = new FileSaveService(root);
            await saves.SaveAsync(0, new() { Snapshot = attached });
            await saves.QuickSaveAsync(new() { Snapshot = attached });
            foreach (var loaded in new[] { await saves.LoadAsync(0), await saves.QuickLoadAsync() })
            {
                var restored = new DialogueHistoryJournal();
                restored.Restore(loaded);
                Assert.That(restored.Lines, Is.EqualTo(new[] { first }));
            }
            journal.Restore(await saves.LoadAsync(0));
            Assert.That(journal.Lines.Count, Is.EqualTo(1));
            Assert.That(() => journal.Restore(new GameSnapshot { States = attached.States }), Throws.TypeOf<InvalidDataException>());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test]
    public async Task CancelledIncompleteTextIsNeverAppended()
    {
        var journal = new DialogueHistoryJournal();
        var presenter = new DeferredText();
        using var registration = new GameRegistration();
        registration.AddBuiltins(dialoguePresenter: presenter);
        registration.RegisterImplementation(DialogueHistoryJournal.CreateModule());
        await using var view = registration.BuildView();
        var runtime = new GameRuntime(null, "main", stateCatalog: view.StateCatalog);
        await using var history = new DialogueHistoryDispatcher(view, journal, new Progress(), CharacterCatalog.Empty);
        using var cancellation = new CancellationTokenSource();
        var instance = history.Dispatch(Text("line#1"), runtime, cancellation.Token);
        Assert.That(journal.Lines, Is.Empty);
        cancellation.Cancel();
        presenter.Finish();
        await instance.PresentationCompletion;
        history.FlushCompleted();
        Assert.That(journal.Lines, Is.Empty);
    }

    private static PrimitiveEntry Audio(string id, string target) => new(BuiltinEntryIds.PlayAudio,
        ValueCodec.SerializeFields(new AudioPlayOptions { TrackId = "vocal", Asset = "voice", Gain = .7f, AutoBlocking = true }))
    { StableId = id, ReadingBoundaryId = target };
    private static PrimitiveEntry Text(string id) => new(BuiltinEntryIds.Text,
        ValueCodec.SerializeFields(new DialogueTextOptions { Content = "<color=red>Hello</color>" }))
    { StableId = id };
    private sealed class Progress : IGameProgressService
    {
        public bool IsRead(string groupId, string entryId) => false;
        public void MarkRead(string groupId, string entryId) { }
    }
    private class ImmediateText : IDialoguePresenter
    {
        public void ShowDialogue() { }
        public void HideDialogue() { }
        public virtual Task PresentTextAsync(string speaker, string text, CancellationToken token) => Task.CompletedTask;
        public Task PresentTextImmediatelyAsync(string speaker, string text, CancellationToken token) => Task.CompletedTask;
        public void SkipText() { }
        public void RevealAllText() { }
    }
    private sealed class DeferredText : ImmediateText
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Finish() => _completion.TrySetResult();
        public override Task PresentTextAsync(string speaker, string text, CancellationToken token) => _completion.Task;
    }
    private sealed class DelayedCompletionReading : PrimitiveInstance, IAdvanceWaitInstance
    {
        private readonly TaskCompletionSource _presentation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool IsBlocking => true;
        public override bool IsSkippable => true;
        public bool IsTextPresented { get; set; }
        public bool IsWaitingForAdvance => IsTextPresented && !IsCompleted;
        public override Task PresentationCompletion => _presentation.Task;
        protected override void OnDispatch() { }
        protected override void OnSkip() => TryComplete();
        public void CompleteAdvance() => Skip();
        public void FinishPresentation() => _presentation.TrySetResult();
    }
}

public sealed class DialogueVoiceReplayTests
{
    [Test]
    public async Task ContinueDuringPendingStartEventuallyStopsTheVoiceAndStaleEventsCannotStopItsReplacement()
    {
        using var audio = new Audio();
        await using var replay = new DialogueVoiceReplay(audio);
        var firstPlay = replay.PlayAsync(new("one", "custom-vocal", .4f));
        await audio.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        replay.Stop();
        Assert.That(replay.IsPlaying, Is.False);
        audio.AllowStart.TrySetResult();
        await firstPlay;
        await replay.PlayAsync(new("two", "custom-vocal", .8f));
        var first = audio.Requests.First();
        audio.Finish(first);
        Assert.That(replay.IsPlaying, Is.True);
        var second = audio.Requests.Last(request => request.Action == AudioBackendAction.Start);
        Assert.That(audio.Requests.Any(request => request.Action == AudioBackendAction.Stop && request.VoiceId == first.VoiceId), Is.True);
        Assert.That(second.TrackId, Is.EqualTo("custom-vocal"));
        Assert.That(second.Gain, Is.EqualTo(.8f));
        audio.Finish(second);
        Assert.That(replay.IsPlaying, Is.False);
    }
    private sealed class Audio : IAudioPlaybackHost
    {
        public AudioBackendCapabilities Capabilities => AudioBackendCapabilities.None;
        public ISEffectCatalog SEffects => new SEffectCatalog([]);
        public event Action<AudioBackendEvent>? EventReceived;
        public List<AudioBackendRequest> Requests { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowStart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task ApplyAsync(AudioBackendRequest request, CancellationToken token = default)
        {
            Requests.Add(request);
            if (request.Action == AudioBackendAction.Start) { Started.TrySetResult(); await AllowStart.Task; }
        }
        public void Finish(AudioBackendRequest request) => EventReceived?.Invoke(new(AudioBackendEventKind.Ended, request.TrackId, request.VoiceId, request.Generation));
        public void Dispose() { }
    }
}
