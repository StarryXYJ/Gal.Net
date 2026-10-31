using GalNet.Media.LibVlc;
using LibVLCSharp.Shared;
using LibVlcMedia = LibVLCSharp.Shared.Media;

namespace GalNet.Avalonia.GameView.Services;

internal sealed class GalleryAudioDurationService(LibVlcFactory factory) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, long> _cache = new(StringComparer.Ordinal);
    private LibVLC? _vlc;
    private bool _disposed;
    public async Task<long> GetAsync(string path, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(path, out var cached)) return cached;
            _vlc ??= factory.Create("--avcodec-hw=none");
            using var media = new LibVlcMedia(_vlc, path);
            // LibVLCSharp completes Parse inline on a native event callback. Never release
            // that Media from the callback that is still dispatching its events.
            var parsed = new TaskCompletionSource<MediaParsedStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = media.Parse(MediaParseOptions.ParseLocal, 5000, lifetime.Token).ContinueWith(task =>
            {
                if (task.IsCanceled) parsed.TrySetCanceled(lifetime.Token);
                else if (task.IsFaulted) parsed.TrySetException(task.Exception!.InnerExceptions);
                else parsed.TrySetResult(task.Result);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            await parsed.Task.ConfigureAwait(false);
            lifetime.Token.ThrowIfCancellationRequested();
            var duration = Math.Max(0, media.Duration);
            if (duration > 0) _cache[path] = duration;
            return duration;
        }
        finally { _gate.Release(); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _gate.Wait();
        try { _vlc?.Dispose(); _cache.Clear(); }
        finally { _gate.Release(); }
    }
}
