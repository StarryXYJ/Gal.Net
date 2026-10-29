using GalNet.Contracts.Builtins.Audio;

namespace GalNet.Primitives.Builtins.Audio;

/// <summary>Orders commands per voice, cancels pending starts and drains backend work using the audio runtime's gate.</summary>
internal sealed class AudioBackendDispatcher(IAudioPlaybackHost? host, object gate, Action<string> report, Action<AudioBackendEvent> terminal)
{
    private readonly IAudioPlaybackHost? _host = host;
    private readonly object _gate = gate;
    private readonly Action<string> _report = report;
    private readonly Action<AudioBackendEvent> _terminal = terminal;
    private readonly Dictionary<string, BackendQueue> _backendQueues = new(StringComparer.Ordinal);
    private readonly List<Task> _pendingRequests = [];
    private Task _mixerTail = Task.CompletedTask;

    public void CompleteVoice(string voiceId)
    {
        lock (_gate)
        {
            if (!_backendQueues.Remove(voiceId, out var queue)) return;
            queue.StartCancellation.Cancel();
            // Native events may arrive synchronously inside ApplyAsync before Tail is assigned.
            // Acquiring the shared gate from a worker observes the completed submission first.
            var cleanup = Task.Run(async () =>
            {
                Task tail;
                lock (_gate) tail = queue.Tail;
                try { await tail.ConfigureAwait(false); }
                finally { queue.StartCancellation.Dispose(); }
            });
            _pendingRequests.Add(cleanup);
        }
    }

    public Task DrainAsync()
    {
        lock (_gate) return Task.WhenAll(_pendingRequests.ToArray());
    }

    public void Enqueue(AudioBackendRequest request)
    {
        lock (_gate)
        {
            if (_host is null)
            {
                _report($"No audio playback host is available for '{request.TrackId}'.");
                return;
            }
            if (request.Action is AudioBackendAction.ApplySEffect or AudioBackendAction.StopSEffect && !_host.Capabilities.SupportsTrackSEffects)
            {
                _report($"Audio backend does not support SEffect on '{request.TrackId}'.");
                return;
            }
            if (_host.Capabilities.SupportsMixer && request.Action is AudioBackendAction.Configure or AudioBackendAction.SetMix or AudioBackendAction.ResetMixer or AudioBackendAction.SetLevel or AudioBackendAction.ApplySEffect or AudioBackendAction.StopSEffect)
            {
                _mixerTail = SendMixerAsync(_mixerTail, request);
                _pendingRequests.RemoveAll(task => task.IsCompleted);
                _pendingRequests.Add(_mixerTail);
                return;
            }
            if (request.FadeSeconds > 0 && !_host.Capabilities.SupportsFade)
            {
                _report($"Audio backend does not support fades on '{request.TrackId}'; applying immediately.");
                request = request with { FadeSeconds = 0 };
            }
            if (!_backendQueues.TryGetValue(request.VoiceId, out var queue))
            {
                queue = new BackendQueue();
                _backendQueues.Add(request.VoiceId, queue);
            }
            // Stop invalidates an in-flight asset load immediately; subsequent commands
            // for this voice still run in order after the producer has observed cancellation.
            if (request.Action == AudioBackendAction.Stop) queue.StartCancellation.Cancel();
            queue.Tail = SendAsync(Task.WhenAll(queue.Tail, _mixerTail), queue, request);
            _pendingRequests.RemoveAll(task => task.IsCompleted);
            _pendingRequests.Add(queue.Tail);
        }
    }

    private async Task SendMixerAsync(Task previous, AudioBackendRequest request)
    {
        try { await previous.ConfigureAwait(false); await _host!.ApplyAsync(request).ConfigureAwait(false); }
        catch (Exception error) { _report($"Audio mixer request failed: {error.Message}"); }
    }

    private async Task SendAsync(Task previous, BackendQueue queue, AudioBackendRequest request)
    {
        try
        {
            await previous.ConfigureAwait(false);
            var cancellation = request.Action == AudioBackendAction.Start ? queue.StartCancellation.Token : CancellationToken.None;
            cancellation.ThrowIfCancellationRequested();
            await _host!.ApplyAsync(request, cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (queue.StartCancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _report($"Audio backend request failed: {exception.Message}");
            if (request.Action == AudioBackendAction.Start)
                _terminal(new(AudioBackendEventKind.Failed, request.TrackId, request.VoiceId, request.Generation, exception.Message));
        }
        finally
        {
            if (request.Action == AudioBackendAction.Stop)
            {
                lock (_gate)
                    if (_backendQueues.GetValueOrDefault(request.VoiceId) == queue) _backendQueues.Remove(request.VoiceId);
                queue.StartCancellation.Dispose();
            }
        }
    }

    private sealed class BackendQueue
    {
        public Task Tail { get; set; } = Task.CompletedTask;
        public CancellationTokenSource StartCancellation { get; } = new();
    }

}
