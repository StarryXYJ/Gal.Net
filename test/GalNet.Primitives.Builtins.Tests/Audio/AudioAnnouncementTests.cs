using System.Threading.Channels;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Runtime;
using GalNet.Primitives.Builtins.Audio;

namespace GeneralTest.Runtime;

public sealed class AudioAnnouncementTests
{
    private static readonly AudioConfiguration Tracks = new([new() { Id = "music", MaxQueuedItems = 4, PreserveDuringCatchUp = true }]);

    [Test]
    public async Task QueuedNamesWaitForSuccessfulStartAndDoNotRepeatOnLoopsOrResume()
    {
        var host = new Host();
        await using var runtime = new AudioRuntime(host);
        var names = Channel.CreateUnbounded<string>();
        runtime.PlaybackStarted += name => names.Writer.TryWrite(name);
        runtime.Play(Tracks, new("music", "a", AudioPlacement.Enqueue, LoopCount: 0) { DisplayName = " First " });
        var first = await host.StartAsync();
        runtime.Play(Tracks, new("music", "b", AudioPlacement.Enqueue) { DisplayName = "Second" });
        Assert.That(names.Reader.TryRead(out _), Is.False);
        host.Raise(first, AudioBackendEventKind.Started);
        Assert.That(await names.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)), Is.EqualTo("First"));
        host.Raise(first, AudioBackendEventKind.Started);
        runtime.Pause(Tracks, "music"); runtime.Resume(Tracks, "music");
        host.Raise(first, AudioBackendEventKind.Ended);
        var loop = await host.StartAsync();
        host.Raise(loop, AudioBackendEventKind.Started);
        runtime.Next(Tracks, "music");
        var second = await host.StartAsync();
        host.Raise(second, AudioBackendEventKind.Started);
        Assert.That(await names.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)), Is.EqualTo("Second"));
        Assert.That(names.Reader.TryRead(out _), Is.False);
    }

    [Test]
    public async Task FailedStaleBlankAndCatchUpStartsDoNotAnnounce()
    {
        var host = new Host();
        await using var runtime = new AudioRuntime(host);
        var names = Channel.CreateUnbounded<string>();
        runtime.PlaybackStarted += name => names.Writer.TryWrite(name);
        runtime.Play(Tracks, new("music", "bad", AudioPlacement.Replace) { DisplayName = "Bad" });
        var failed = await host.StartAsync();
        host.Raise(failed, AudioBackendEventKind.Failed);
        host.Raise(failed, AudioBackendEventKind.Started);
        runtime.Play(Tracks, new("music", "blank", AudioPlacement.Replace) { DisplayName = "  " });
        host.Raise(await host.StartAsync(), AudioBackendEventKind.Started);
        runtime.BeginCatchUp();
        runtime.Play(Tracks, new("music", "restore", AudioPlacement.Replace) { DisplayName = "Restored" });
        runtime.EndCatchUp();
        host.Raise(await host.StartAsync(), AudioBackendEventKind.Started);
        runtime.Play(Tracks, new("music", "valid", AudioPlacement.Replace) { DisplayName = "Valid" });
        host.Raise(await host.StartAsync(), AudioBackendEventKind.Started);
        Assert.That(await names.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)), Is.EqualTo("Valid"));
        Assert.That(names.Reader.TryRead(out _), Is.False);
    }

    [Test]
    public async Task RestoringSavedNamesDoesNotReplayAnnouncements()
    {
        var builder = new StateCatalogBuilder();
        builder.Register<AudioTrackState>("builtin.audio.tracks", value => value.Id, AudioRuntime.ValidateTrack);
        builder.Register<AudioSEffectState>("builtin.audio.seffects", value => value.Key());
        builder.Register<AudioChannelState>("builtin.audio.channels", value => value.Key());
        var state = new GameState(builder.Build());
        var host = new Host();
        await using var runtime = new AudioRuntime(host);
        runtime.Bind(state);
        var names = Channel.CreateUnbounded<string>();
        runtime.PlaybackStarted += name => names.Writer.TryWrite(name);
        runtime.Play(Tracks, new("music", "saved", AudioPlacement.Replace) { DisplayName = "Saved" });
        var old = await host.StartAsync();
        await runtime.RestoreAsync(state, Tracks, CancellationToken.None);
        var restored = await host.StartAsync();
        host.Raise(old, AudioBackendEventKind.Started);
        host.Raise(restored, AudioBackendEventKind.Started);
        runtime.Play(Tracks, new("music", "fresh", AudioPlacement.Replace) { DisplayName = "Fresh" });
        host.Raise(await host.StartAsync(), AudioBackendEventKind.Started);
        Assert.That(await names.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)), Is.EqualTo("Fresh"));
        Assert.That(names.Reader.TryRead(out _), Is.False);
    }

    [Test]
    public async Task SynchronousBackendDoesNotRunObserversUnderTheSubmissionLock()
    {
        var host = new Host { StartImmediately = true };
        await using var runtime = new AudioRuntime(host);
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.PlaybackStarted += _ =>
        {
            // A worker can acquire the audio gate while this observer is running.
            Assert.That(Task.Run(() => runtime.Pause(Tracks, "music")).Wait(TimeSpan.FromSeconds(2)), Is.True);
            observed.TrySetResult();
        };
        runtime.Play(Tracks, new("music", "a", AudioPlacement.Replace) { DisplayName = "A" });
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    private sealed class Host : IAudioPlaybackHost
    {
        private readonly Channel<AudioBackendRequest> _starts = Channel.CreateUnbounded<AudioBackendRequest>();
        public bool StartImmediately { get; init; }
        public AudioBackendCapabilities Capabilities { get; } = new(true, true);
        public ISEffectCatalog SEffects { get; } = SEffectCatalog.Builtins;
        public event Action<AudioBackendEvent>? EventReceived;
        public Task<AudioBackendRequest> StartAsync() => _starts.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        public void Raise(AudioBackendRequest request, AudioBackendEventKind kind) => EventReceived?.Invoke(new(kind, request.TrackId, request.VoiceId, request.Generation));
        public Task ApplyAsync(AudioBackendRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Action == AudioBackendAction.Start)
            {
                _starts.Writer.TryWrite(request);
                if (StartImmediately) Raise(request, AudioBackendEventKind.Started);
            }
            return Task.CompletedTask;
        }
        public void Dispose() { }
    }
}
