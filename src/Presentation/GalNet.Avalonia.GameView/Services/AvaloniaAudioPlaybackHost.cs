using System.Threading.Channels;
using GalNet.Assets;
using GalNet.Core.Assets;
using GalNet.Core.Audio;
using GalNet.Presentation.Abstractions.Audio;
using LibVLCSharp.Shared;
using LibVlcMedia = LibVLCSharp.Shared.Media;

namespace GalNet.Avalonia.GameView.Services;

/// <summary>LibVLC-backed audio host. Logical queue and track ownership remains in AudioRuntime.</summary>
public sealed class AvaloniaAudioPlaybackHost : IAudioPlaybackHost, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly IAssetFileMaterializer _files;
    private readonly bool _ownsFiles;
    private readonly Dictionary<string, Voice> _voices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AssetFileLease> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PendingLoad> _pending = new(StringComparer.Ordinal);
    private readonly List<Task> _loads = [];
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _materialization = new(1, 1);
    private readonly Channel<(Voice Voice, AudioBackendEventKind Kind, string? Message)> _terminalEvents =
        Channel.CreateUnbounded<(Voice, AudioBackendEventKind, string?)>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _terminalWorker;
    private Task? _disposeTask;
    private readonly AvaloniaAudioSEffectRegistry _effects;
    private readonly LibVLC? _libVlc;
    private bool _disposed;

    public AvaloniaAudioPlaybackHost(IAssetManager assets, IEnumerable<IAudioSEffectProcessor>? effectProcessors = null, IAssetFileMaterializer? files = null)
    {
        _files = files ?? new AssetFileMaterializer(assets);
        _ownsFiles = files is null;
        _effects = new AvaloniaAudioSEffectRegistry(AvaloniaAudioSEffectRegistry.DefaultProcessors.Concat(effectProcessors ?? []));
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            _libVlc = new LibVLC("--no-video-title-show");
            Capabilities = new(true, true, true);
            SEffects = _effects.Catalog;
        }
        catch (Exception exception)
        {
            InitializationError = exception.Message;
            Capabilities = AudioBackendCapabilities.None;
            SEffects = SEffectCatalog.Empty;
        }
        _terminalWorker = Task.Run(DrainTerminalEventsAsync);
    }

    public AudioBackendCapabilities Capabilities { get; }
    public ISEffectCatalog SEffects { get; }
    public string? InitializationError { get; }
    public event Action<AudioBackendEvent>? EventReceived;

    public async Task ApplyAsync(AudioBackendRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        switch (request.Action)
        {
            case AudioBackendAction.Start:
                await StartTrackedAsync(request, cancellationToken).ConfigureAwait(false);
                break;
            case AudioBackendAction.Stop:
                Stop(request, request.FadeSeconds);
                break;
            case AudioBackendAction.Pause:
                WithVoice(request, voice => voice.Player.Pause());
                break;
            case AudioBackendAction.Resume:
                WithVoice(request, voice => voice.Player.Play());
                break;
            case AudioBackendAction.SetGain:
                WithVoice(request, voice => { voice.BaseGain = request.Gain; SetVolume(voice); });
                break;
            case AudioBackendAction.ApplySEffect:
                ApplySEffect(request);
                break;
            case AudioBackendAction.StopSEffect:
                StopSEffect(request);
                break;
        }
    }

    public void Dispose() => _ = DisposeAsync().AsTask();

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeTask is not null) return new ValueTask(_disposeTask);
            _disposed = true;
            _shutdown.Cancel();
            foreach (var voice in _voices.Values) voice.Dispose();
            _voices.Clear();
            _terminalEvents.Writer.TryComplete();
            _disposeTask = FinishDisposalAsync(_loads.ToArray());
            return new ValueTask(_disposeTask);
        }
    }

    private async Task FinishDisposalAsync(Task[] loads)
    {
        // Ensure cancellation callbacks have left the disposal lock before cleanup.
        await Task.Yield();
        try { await Task.WhenAll(loads.Append(_terminalWorker)).ConfigureAwait(false); }
        finally
        {
            foreach (var lease in _paths.Values) lease.Dispose();
            _paths.Clear();
            _libVlc?.Dispose();
            _materialization.Dispose();
            _shutdown.Dispose();
            if (_ownsFiles) await _files.DisposeAsync().ConfigureAwait(false);
        }
    }

    private Task StartTrackedAsync(AudioBackendRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pending.TryGetValue(request.VoiceId, out var previous)) previous.Cancellation.Cancel();
            var pending = new PendingLoad(request.Generation,
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token));
            _pending[request.VoiceId] = pending;
            var task = ObserveStartAsync(request, pending);
            _loads.RemoveAll(load => load.IsCompleted);
            _loads.Add(task);
            return task;
        }
    }

    private async Task ObserveStartAsync(AudioBackendRequest request, PendingLoad pending)
    {
        try { await StartAsync(request, pending.Cancellation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (pending.Cancellation.IsCancellationRequested) { }
        finally
        {
            lock (_gate)
                if (_pending.TryGetValue(request.VoiceId, out var current) && ReferenceEquals(current, pending))
                    _pending.Remove(request.VoiceId);
            pending.Cancellation.Dispose();
        }
    }

    private async Task StartAsync(AudioBackendRequest request, CancellationToken cancellationToken)
    {
        if (_libVlc is null || string.IsNullOrWhiteSpace(request.AssetId))
        {
            Raise(new(AudioBackendEventKind.Failed, request.TrackId, request.VoiceId, request.Generation,
                InitializationError ?? "LibVLC audio is unavailable."));
            return;
        }

        string path;
        try { path = await MaterializeAsync(request.AssetId, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            Raise(new(AudioBackendEventKind.Failed, request.TrackId, request.VoiceId, request.Generation, exception.Message));
            return;
        }

        Voice voice;
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed) return;
            RemoveVoice(request.VoiceId);
            var player = new MediaPlayer(_libVlc);
            voice = new Voice(request, player);
            player.EndReached += (_, _) => Ended(voice);
            player.EncounteredError += (_, _) => Failed(voice, "LibVLC could not play the audio asset.");
            _voices.Add(request.VoiceId, voice);
            voice.BaseGain = request.Gain;
            voice.FadeGain = request.FadeSeconds > 0 ? 0 : 1;
            ReapplySEffects(voice);
            using var media = new LibVlcMedia(_libVlc, path);
            player.Play(media);
        }
        Raise(new(AudioBackendEventKind.Started, request.TrackId, request.VoiceId, request.Generation));
        if (request.FadeSeconds > 0) _ = FadeAsync(voice, 0, 1, request.FadeSeconds, stopWhenDone: false);
    }

    private async Task<string> MaterializeAsync(string assetId, CancellationToken cancellationToken)
    {
        await _materialization.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
                if (_paths.TryGetValue(assetId, out var existing)) return existing.Path;
            var lease = await _files.AcquireAsync(assetId, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (_disposed) { lease.Dispose(); throw new ObjectDisposedException(nameof(AvaloniaAudioPlaybackHost)); }
                _paths.Add(assetId, lease);
                return lease.Path;
            }
        }
        finally { _materialization.Release(); }
    }

    private void Stop(AudioBackendRequest request, float fadeSeconds)
    {
        lock (_gate)
            if (_pending.TryGetValue(request.VoiceId, out var pending) && pending.Generation == request.Generation)
                pending.Cancellation.Cancel();
        WithVoice(request, voice =>
        {
            if (fadeSeconds <= 0)
            {
                voice.Player.Stop();
                RemoveVoice(voice.Request.VoiceId);
                return;
            }
            _ = FadeAsync(voice, voice.FadeGain, 0, fadeSeconds, stopWhenDone: true);
        });
    }

    private void ApplySEffect(AudioBackendRequest request) => WithVoice(request, voice =>
    {
        if (string.IsNullOrWhiteSpace(request.SEffectId) || string.IsNullOrWhiteSpace(request.SEffectInstanceId) || request.SEffectParameters is not { } parameters || !_effects.TryGet(request.SEffectId, out _)) return;
        voice.Effects[request.SEffectInstanceId] = new(request.SEffectId, parameters);
        ReapplySEffects(voice);
    });

    private void StopSEffect(AudioBackendRequest request) => WithVoice(request, voice =>
    {
        if (!string.IsNullOrWhiteSpace(request.SEffectInstanceId) && voice.Effects.Remove(request.SEffectInstanceId)) ReapplySEffects(voice);
    });

    private async Task FadeAsync(Voice voice, float from, float to, float seconds, bool stopWhenDone)
    {
        const int steps = 20;
        CancellationToken cancellation;
        lock (_gate)
        {
            if (_disposed || !_voices.TryGetValue(voice.Request.VoiceId, out var current) || !ReferenceEquals(current, voice)) return;
            cancellation = voice.BeginFade();
        }
        try
        {
            for (var index = 1; index <= steps; index++)
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds / steps), cancellation).ConfigureAwait(false);
                lock (_gate)
                {
                    if (_disposed || !_voices.TryGetValue(voice.Request.VoiceId, out var current) || !ReferenceEquals(current, voice)) return;
                    voice.FadeGain = from + (to - from) * index / steps;
                    SetVolume(voice);
                }
            }
            if (stopWhenDone)
            {
                lock (_gate)
                {
                    if (_voices.TryGetValue(voice.Request.VoiceId, out var current) && ReferenceEquals(current, voice))
                    {
                        voice.Player.Stop();
                        RemoveVoice(voice.Request.VoiceId);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void WithVoice(AudioBackendRequest request, Action<Voice> action)
    {
        lock (_gate)
            if (_voices.TryGetValue(request.VoiceId, out var voice) && voice.Request.Generation == request.Generation) action(voice);
    }

    private void Ended(Voice voice)
    {
        _terminalEvents.Writer.TryWrite((voice, AudioBackendEventKind.Ended, null));
    }

    private void Failed(Voice voice, string message)
    {
        _terminalEvents.Writer.TryWrite((voice, AudioBackendEventKind.Failed, message));
    }

    private async Task DrainTerminalEventsAsync()
    {
        // LibVLC callbacks must not Stop/Dispose a player or wait for our gate:
        // player disposal can itself wait for the native callback to return.
        await foreach (var item in _terminalEvents.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            lock (_gate)
            {
                if (_disposed || !_voices.TryGetValue(item.Voice.Request.VoiceId, out var current) ||
                    !ReferenceEquals(current, item.Voice)) continue;
                RemoveVoice(item.Voice.Request.VoiceId);
            }
            Raise(new(item.Kind, item.Voice.Request.TrackId, item.Voice.Request.VoiceId,
                item.Voice.Request.Generation, item.Message));
        }
    }

    private void RemoveVoice(string voiceId)
    {
        if (_voices.Remove(voiceId, out var voice)) voice.Dispose();
    }

    private void ReapplySEffects(Voice voice)
    {
        using var equalizer = new Equalizer();
        var state = new AvaloniaAudioSEffectState(Enumerable.Range(0, (int)equalizer.BandCount).Select(index => equalizer.BandFrequency((uint)index)).ToArray());
        foreach (var effect in voice.Effects.Values)
            if (_effects.TryGet(effect.Id, out var processor)) processor.Apply(effect.Parameters, state);
        voice.EffectGain = state.Gain;
        if (state.HasEqualizerAdjustment)
        {
            for (var index = 0; index < state.EqualizerBandFrequencies.Count; index++) equalizer.SetAmp(state.BandGain(index), (uint)index);
            voice.Player.SetEqualizer(equalizer);
        }
        else voice.Player.UnsetEqualizer();
        SetVolume(voice);
    }

    private static float CurrentGain(Voice voice) => voice.BaseGain * voice.FadeGain * voice.EffectGain;
    private static int ToVolume(float gain) => (int)Math.Round(Math.Clamp(gain, 0f, 1f) * 100);
    private static void SetVolume(Voice voice) => voice.Player.Volume = ToVolume(CurrentGain(voice));
    private void Raise(AudioBackendEvent @event) => EventReceived?.Invoke(@event);

    private sealed class Voice(AudioBackendRequest request, MediaPlayer player) : IDisposable
    {
        public AudioBackendRequest Request { get; } = request;
        public MediaPlayer Player { get; } = player;
        public float BaseGain { get; set; }
        public float FadeGain { get; set; } = 1;
        public float EffectGain { get; set; } = 1;
        public Dictionary<string, ActiveSEffect> Effects { get; } = new(StringComparer.Ordinal);
        private CancellationTokenSource FadeCancellation { get; set; } = new();
        public CancellationToken BeginFade()
        {
            FadeCancellation.Cancel();
            FadeCancellation.Dispose();
            FadeCancellation = new CancellationTokenSource();
            return FadeCancellation.Token;
        }
        public void Dispose()
        {
            FadeCancellation.Cancel();
            FadeCancellation.Dispose();
            Player.Dispose();
        }
    }

    private sealed record ActiveSEffect(string Id, System.Text.Json.JsonElement Parameters);
    private sealed record PendingLoad(long Generation, CancellationTokenSource Cancellation);
}
