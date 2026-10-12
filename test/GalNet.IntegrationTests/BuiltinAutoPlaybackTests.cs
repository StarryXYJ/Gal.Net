using System.Text.Json;
using GalNet.Core.Audio;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Core.Settings;
using GalNet.Presentation.Abstractions.Audio;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Engine;

namespace GeneralTest.Runtime;

public sealed class BuiltinAutoPlaybackTests
{
    [Test]
    public async Task AutoIntervalStartsOnlyAfterActualTextAndVoicePrimitivesFinish(
        [Values(false, true)] bool voiceFirst, [Values(false, true)] bool voiceFails)
    {
        var text = new ControlledText();
        using var audio = new ControlledAudio();
        await using var view = new GameRegistration().AddBuiltins(dialoguePresenter: text, audioHost: audio).BuildView();
        var settings = new SettingsContainer();
        settings.Set(new ProjectSettings());
        settings.Set(new GameSettings { AutoAdvanceIntervalMilliseconds = 120 });
        var group = new Group { Id = "main" };
        group.Entries.AddRange([
            new PrimitiveEntry("audio.play", JsonSerializer.SerializeToElement(new { trackId = AudioBuses.Vocal, asset = "voice", autoBlocking = "true" })) { StableId = "voice" },
            new PrimitiveEntry("dialogue.text", JsonSerializer.SerializeToElement(new { content = "Hello" })) { StableId = "text" }
        ]);
        var graph = new Graph { RootNodeId = "main", Nodes = { group } };
        await using var engine = new GameEngine(graph, view, settings: settings);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.StateChanged += state => { if (state.State == GameEngineState.Completed) ended.TrySetResult(); };
        await engine.AdvanceAsync();
        await audio.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await engine.SetPlaybackModeAsync(PlaybackMode.Auto);

        if (voiceFirst) audio.Finish(voiceFails);
        else text.Finish();
        await Task.Delay(180);
        Assert.That(ended.Task.IsCompleted, Is.False, "The other presentation must still block automatic advance.");
        if (voiceFirst) text.Finish();
        else audio.Finish(voiceFails);
        await Task.Delay(30);
        Assert.That(ended.Task.IsCompleted, Is.False, "The interval begins after both presentations, not when Auto is selected.");
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(engine.PlaybackMode, Is.EqualTo(PlaybackMode.Manual));
    }

    private sealed class ControlledText : IDialoguePresenter
    {
        private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Finish() => _finished.TrySetResult();
        public void ShowDialogue() { }
        public void HideDialogue() { }
        public void SkipText() { }
        public void RevealAllText() => Finish();
        public Task PresentTextAsync(string speaker, string text, CancellationToken cancellationToken) => _finished.Task.WaitAsync(cancellationToken);
        public Task PresentTextImmediatelyAsync(string speaker, string text, CancellationToken cancellationToken) { Finish(); return Task.CompletedTask; }
    }

    private sealed class ControlledAudio : IAudioPlaybackHost
    {
        public AudioBackendCapabilities Capabilities { get; } = AudioBackendCapabilities.None;
        public ISEffectCatalog SEffects { get; } = new SEffectCatalog([]);
        public event Action<AudioBackendEvent>? EventReceived;
        public TaskCompletionSource<AudioBackendRequest> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ApplyAsync(AudioBackendRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Action == AudioBackendAction.Start) Started.TrySetResult(request);
            return Task.CompletedTask;
        }
        public void Finish(bool failure)
        {
            var start = Started.Task.Result;
            EventReceived?.Invoke(new(failure ? AudioBackendEventKind.Failed : AudioBackendEventKind.Ended,
                start.TrackId, start.VoiceId, start.Generation));
        }
        public void Dispose() { }
    }
}
