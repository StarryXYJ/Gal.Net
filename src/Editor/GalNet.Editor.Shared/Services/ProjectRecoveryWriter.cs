using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Services;

/// <summary>Serializes persistence of detached snapshots; never calls back into the author/UI thread.</summary>
internal sealed class ProjectRecoveryWriter(Action<EditorProjectDocument> persist) : IDisposable
{
    private readonly object _sync = new();
    private Task _pending = Task.CompletedTask;
    private bool _stopped;
    private Exception? _lastError;

    public Exception? LastError => Volatile.Read(ref _lastError);

    public Task WriteAsync(EditorProjectDocument snapshot)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_stopped, this);
            var previous = _pending;
            return _pending = Task.Run(async () =>
            {
                await previous.ConfigureAwait(false);
                try { persist(snapshot); Volatile.Write(ref _lastError, null); }
                catch (Exception error) { Volatile.Write(ref _lastError, error); }
            });
        }
    }

    public Task StopAsync()
    {
        lock (_sync) { _stopped = true; return _pending; }
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();
}
