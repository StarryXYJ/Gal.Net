using GalNet.Contracts.Builtins.Audio;
using GalNet.Presentation.Abstractions.Application;

namespace GalNet.GameHost;

/// <summary>A single transient backend voice, borrowing the running game's track mixer.</summary>
public sealed class DialogueVoiceReplay : IAsyncDisposable
{
    private readonly IAudioPlaybackHost _host;
    private readonly object _gate = new();
    private AudioBackendRequest? _current;
    private Task _operations = Task.CompletedTask;
    private long _revision;
    private bool _disposed;
    private CancellationTokenSource? _starting;
    public event Action? Changed;
    public event Action<Exception>? Failed;
    public bool IsPlaying { get { lock (_gate) return _current is not null; } }

    public DialogueVoiceReplay(IAudioPlaybackHost host)
    {
        _host = host;
        host.EventReceived += OnEvent;
    }

    public Task PlayAsync(DialogueVoice voice)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StopCore();
            var request = new AudioBackendRequest(AudioBackendAction.Start, voice.TrackId,
                "dialogue-replay-" + Guid.NewGuid().ToString("N"), ++_revision, voice.AssetId, voice.Gain);
            _current = request;
            var cancellation = new CancellationTokenSource();
            _starting = cancellation;
            _operations = StartAfterAsync(_operations, request, cancellation);
            Changed?.Invoke();
            return _operations;
        }
    }

    public void Stop()
    {
        lock (_gate) StopCore();
    }

    private void StopCore()
    {
        if (_current is not { } previous) return;
        _current = null;
        _revision++;
        _starting?.Cancel();
        _operations = StopAfterAsync(_operations, previous);
        Changed?.Invoke();
    }

    private async Task StartAfterAsync(Task previous, AudioBackendRequest request, CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Yield();
            await previous.ConfigureAwait(false);
            lock (_gate) { if (!ReferenceEquals(_current, request) || _disposed) return; }
            await _host.ApplyAsync(request, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            lock (_gate) { if (ReferenceEquals(_current, request)) _current = null; }
            Changed?.Invoke();
            Failed?.Invoke(error);
        }
        finally
        {
            lock (_gate) { if (ReferenceEquals(_starting, cancellation)) _starting = null; }
            cancellation.Dispose();
        }
    }

    private async Task StopAfterAsync(Task previous, AudioBackendRequest request)
    {
        await Task.Yield();
        await previous.ConfigureAwait(false);
        try { await _host.ApplyAsync(request with { Action = AudioBackendAction.Stop }).ConfigureAwait(false); }
        catch (Exception error) { Failed?.Invoke(error); }
    }

    private void OnEvent(AudioBackendEvent value)
    {
        lock (_gate)
        {
            if (_current is not { } current || value.VoiceId != current.VoiceId || value.Generation != current.Generation ||
                value.TrackId != current.TrackId || value.Kind is not (AudioBackendEventKind.Ended or AudioBackendEventKind.Failed)) return;
            _current = null;
        }
        Changed?.Invoke();
        if (value.Kind == AudioBackendEventKind.Failed) Failed?.Invoke(new InvalidOperationException(value.Message ?? "Voice replay failed."));
    }

    public async ValueTask DisposeAsync()
    {
        Task operations;
        lock (_gate) { if (_disposed) return; _disposed = true; StopCore(); operations = _operations; }
        _host.EventReceived -= OnEvent;
        await operations.ConfigureAwait(false);
    }
}
