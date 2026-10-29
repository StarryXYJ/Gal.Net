using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Assets;

namespace GalNet.Media.Audio;

/// <summary>Owns streaming sources and terminal delivery, independently of UI and device APIs.</summary>
public class PcmAudioPlaybackHost : IAudioPlaybackHost, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly IAssetFileMaterializer _files;
    private readonly Func<string, IAudioPcmDecoder> _decode;
    private readonly IAudioOutput _output;
    private readonly PcmMixer _mixer = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<string, Playback> _playbacks = new(StringComparer.Ordinal);
    private readonly List<Task> _loads = [];
    private readonly System.Collections.Concurrent.ConcurrentQueue<Exception> _cleanupErrors = new();
    private readonly Dictionary<string, (long Generation, CancellationTokenSource Cancel)> _pending = new(StringComparer.Ordinal);
    private readonly Task _terminalWorker;
    private Task? _dispose;
    private bool _disposed;
    private Exception? _renderError;
    private bool _outputSuspended;
    public AudioBackendCapabilities Capabilities { get; }
    public ISEffectCatalog SEffects => SEffectCatalog.Builtins;
    public string? InitializationError { get; }
    public event Action<AudioBackendEvent>? EventReceived;
    public bool SupportsOutputSuspension => _output is IAudioOutputLifecycle;

    /// <summary>Stops device pulls without changing story pauses, voice positions or DSP state.</summary>
    public void SuspendOutput()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (InitializationError is not null) throw new InvalidOperationException(InitializationError);
            if (_output is not IAudioOutputLifecycle lifecycle) throw new NotSupportedException("This audio output does not support suspension.");
            if (_outputSuspended) return;
            lifecycle.SuspendOutput();
            _outputSuspended = true;
        }
    }

    public void ResumeOutput()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (InitializationError is not null) throw new InvalidOperationException(InitializationError);
            if (_output is not IAudioOutputLifecycle lifecycle) throw new NotSupportedException("This audio output does not support suspension.");
            if (!_outputSuspended) return;
            lifecycle.ResumeOutput();
            _outputSuspended = false;
        }
    }

    public PcmAudioPlaybackHost(IAssetFileMaterializer files, Func<string, IAudioPcmDecoder> decode, IAudioOutput output)
    {
        _files = files; _decode = decode; _output = output;
        Capabilities = new(true, true, true, true); // DSP contracts remain valid without a physical device.
        try { output.Start(Render); }
        catch (Exception error) { InitializationError = error.Message; output.Dispose(); }
        _terminalWorker = Task.Run(TerminalsAsync);
    }
    public Task ApplyAsync(AudioBackendRequest request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Action == AudioBackendAction.Start)
            {
                if (_pending.TryGetValue(request.VoiceId, out var old)) old.Cancel.Cancel();
                var pending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
                _pending[request.VoiceId] = (request.Generation, pending);
                var load = Task.Run(() => StartAsync(request, pending), CancellationToken.None);
                _loads.RemoveAll(task => task.IsCompleted); _loads.Add(load); return load;
            }
            switch (request.Action)
            {
                case AudioBackendAction.Configure: _mixer.Configure(request.Configuration ?? throw new InvalidDataException("Missing audio configuration.")); break;
                case AudioBackendAction.SetMix: _mixer.ApplyMix(request.Mix ?? new()); break;
                case AudioBackendAction.ResetMixer:
                    foreach (var pending in _pending.Values) pending.Cancel.Cancel();
                    foreach (var oldPlayback in _playbacks.Values) oldPlayback.Voice.Abandon();
                    _mixer.Reset(); Volatile.Write(ref _renderError, null); break;
                case AudioBackendAction.ApplySEffect:
                    var parameters = request.SEffectParameters ?? throw new InvalidDataException("Missing audio effect parameters.");
                    var diagnostics = SEffects.Validate(request.SEffectId ?? "", request.Scope, parameters);
                    if (diagnostics.Count != 0) throw new InvalidDataException(string.Join(" ", diagnostics));
                    _mixer.Target(request.Scope, request.TrackId).ApplyEffect(new(request.TrackId, request.SEffectInstanceId ?? "", request.SEffectId!, parameters, request.Scope)); break;
                case AudioBackendAction.StopSEffect: _mixer.Target(request.Scope, request.TrackId).StopEffect(request.SEffectInstanceId ?? ""); break;
                case AudioBackendAction.SetLevel: _mixer.Target(request.Scope, request.TrackId).ContentLevel = new(request.Gain, request.Muted); break;
                default:
                    if (request.Action == AudioBackendAction.Stop && _pending.TryGetValue(request.VoiceId, out var loading) && loading.Generation == request.Generation) loading.Cancel.Cancel();
                    if (_playbacks.TryGetValue(request.VoiceId, out var playback) && playback.Voice.Request.Generation == request.Generation)
                        switch (request.Action)
                        {
                            case AudioBackendAction.Stop: playback.Voice.Stop(request.FadeSeconds); break;
                            case AudioBackendAction.Pause: playback.Voice.Paused = true; break;
                            case AudioBackendAction.Resume: playback.Voice.Paused = false; break;
                            case AudioBackendAction.SetGain: playback.Voice.Gain = request.Gain; break;
                        }
                    break;
            }
            return Task.CompletedTask;
        }
    }
    private async Task StartAsync(AudioBackendRequest request, CancellationTokenSource pending)
    {
        var linked = pending;
        AssetFileLease? lease = null;
        StreamingAudioSource? source = null;
        try
        {
            if (InitializationError is not null) throw new InvalidOperationException(InitializationError);
            if (Volatile.Read(ref _renderError) is { } renderError) throw new InvalidOperationException("Audio rendering failed.", renderError);
            if (string.IsNullOrWhiteSpace(request.AssetId) || string.IsNullOrWhiteSpace(request.VoiceId) ||
                !float.IsFinite(request.Gain) || request.Gain < 0 || !float.IsFinite(request.FadeSeconds) || request.FadeSeconds < 0 ||
                !float.IsFinite(request.FadeOutSeconds) || request.FadeOutSeconds < 0) throw new InvalidDataException("Invalid audio voice request.");
            lease = await _files.AcquireAsync(request.AssetId ?? "", linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            source = new StreamingAudioSource(_decode(lease.Path), _shutdown.Token);
            await source.PrepareAsync(linked.Token).ConfigureAwait(false);
            Playback playback;
            lock (_gate)
            {
                linked.Token.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
                var voice = new PcmVoice(request, _mixer.Target(SEffectScope.Track, request.TrackId), source);
                playback = new(voice, source, lease);
                _playbacks.Add(request.VoiceId, playback);
                _mixer.Add(voice); source = null; lease = null;
            }
            Raise(new(AudioBackendEventKind.Started, request.TrackId, request.VoiceId, request.Generation));
            Volatile.Write(ref playback.Started, 1);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            if (!_shutdown.IsCancellationRequested) Raise(new(AudioBackendEventKind.Ended, request.TrackId, request.VoiceId, request.Generation));
        }
        catch (Exception error) { Raise(new(AudioBackendEventKind.Failed, request.TrackId, request.VoiceId, request.Generation, error.Message)); }
        finally
        {
            await ReleaseAsync(source, lease).ConfigureAwait(false);
            lock (_gate)
                if (_pending.TryGetValue(request.VoiceId, out var loading) && ReferenceEquals(loading.Cancel, pending)) _pending.Remove(request.VoiceId);
            pending.Dispose();
        }
    }
    private void Render(Span<float> output)
    {
        if (Volatile.Read(ref _renderError) is not null) { output.Clear(); return; }
        try { _mixer.Render(output); }
        catch (Exception error) { output.Clear(); Interlocked.CompareExchange(ref _renderError, error, null); }
    }
    private async Task TerminalsAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(5, _shutdown.Token).ConfigureAwait(false);
                Playback[] ended;
                lock (_gate)
                {
                    if (Volatile.Read(ref _renderError) is not null)
                        foreach (var playback in _playbacks.Values) playback.Voice.Abandon();
                    ended = _playbacks.Values.Where(playback => Volatile.Read(ref playback.Started) != 0 && playback.Voice.Ended).ToArray();
                    foreach (var playback in ended) { _playbacks.Remove(playback.Voice.Request.VoiceId); _mixer.Remove(playback.Voice); }
                }
                foreach (var playback in ended)
                {
                    var request = playback.Voice.Request;
                    var cleanup = await ReleaseAsync(playback.Source, playback.Lease).ConfigureAwait(false);
                    var error = playback.Source.Error ?? Volatile.Read(ref _renderError) ?? cleanup;
                    Raise(new(error is null ? AudioBackendEventKind.Ended : AudioBackendEventKind.Failed,
                        request.TrackId, request.VoiceId, request.Generation, error?.Message));
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
    }
    private async Task<Exception?> ReleaseAsync(StreamingAudioSource? source, AssetFileLease? lease)
    {
        Exception? error = null;
        try { if (source is not null) await source.DisposeAsync().ConfigureAwait(false); }
        catch (Exception failure) { error = failure; _cleanupErrors.Enqueue(failure); }
        try { lease?.Dispose(); }
        catch (Exception failure) { error ??= failure; _cleanupErrors.Enqueue(failure); }
        return error;
    }
    private void Raise(AudioBackendEvent value)
    {
        foreach (var handler in EventReceived?.GetInvocationList() ?? [])
            try { ((Action<AudioBackendEvent>)handler)(value); } catch (Exception) { /* Observers cannot interrupt owned cleanup. */ }
    }
    public void Dispose() => _ = DisposeAsync().AsTask();
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_dispose is not null) return new(_dispose);
            _disposed = true;
            _shutdown.Cancel();
            _dispose = FinishAsync(_loads.ToArray());
            return new(_dispose);
        }
    }
    private async Task FinishAsync(Task[] loads)
    {
        await Task.Yield();
        try { await Task.WhenAll(loads.Append(_terminalWorker)).ConfigureAwait(false); }
        catch (Exception error) { _cleanupErrors.Enqueue(error); }
        finally
        {
            try { _output.Dispose(); } // Waits for the last render callback before releasing sources.
            catch (Exception error) { _cleanupErrors.Enqueue(error); }
            foreach (var playback in _playbacks.Values)
                await ReleaseAsync(playback.Source, playback.Lease).ConfigureAwait(false);
            _playbacks.Clear(); _mixer.Reset(); _shutdown.Dispose();
        }
        if (!_cleanupErrors.IsEmpty) throw new AggregateException("Audio cleanup failed.", _cleanupErrors);
    }
    private sealed record Playback(PcmVoice Voice, StreamingAudioSource Source, AssetFileLease Lease)
    {
        public int Started;
    }
}
