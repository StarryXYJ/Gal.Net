using System.Text.Json;
using GalNet.Runtime.Progress;

namespace GalNet.Storage.FileSystem;

/// <summary>Batches in-memory read progress, periodically flushes it and drains writes on shutdown.</summary>
public sealed class FileGameProgressService : IGameProgressService, IDisposable, IAsyncDisposable
{
    private readonly string _path;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ReadProgressIndex _index;
    private readonly Task _worker;
    private long _flushedRevision;
    private Exception? _lastError;
    private bool _closing;
    private Task? _disposeTask;

    public FileGameProgressService(string profileDirectory, TimeSpan? flushInterval = null)
    {
        var interval = flushInterval ?? TimeSpan.FromSeconds(2);
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(flushInterval));
        _path = Path.Combine(profileDirectory, "progress.json");
        var data = File.Exists(_path)
            ? JsonSerializer.Deserialize<ProgressData>(File.ReadAllText(_path))
                ?? throw new InvalidDataException("Read progress document is empty.")
            : new() { Version = 2 };
        if (data.Version != 2) throw new InvalidDataException($"Unsupported read progress version '{data.Version}'.");
        _index = new ReadProgressIndex(data.ReadEntries.Select(ParseKey));
        _worker = Task.Run(() => RunPersistenceAsync(interval));
    }

    public Exception? LastPersistenceError { get { lock (_sync) return _lastError; } }
    public bool IsRead(string groupId, string entryId) { lock (_sync) return _index.IsRead(groupId, entryId); }
    public void MarkRead(string groupId, string entryId)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            _index.MarkRead(groupId, entryId);
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            long revision;
            ProgressData data;
            lock (_sync)
            {
                revision = _index.Revision;
                if (revision == _flushedRevision) return;
                data = new ProgressData
                {
                    Version = 2,
                    ReadEntries = _index.Snapshot().Select(entry => JsonSerializer.Serialize(new[] { entry.GroupId, entry.EntryId })).ToHashSet()
                };
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporaryPath = _path + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(data), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _path, overwrite: true);
            lock (_sync) { _flushedRevision = revision; _lastError = null; }
        }
        catch (Exception error)
        {
            if (error is not OperationCanceledException) lock (_sync) _lastError = error;
            throw;
        }
        finally { _writeGate.Release(); }
    }

    public void Clear()
    {
        _writeGate.Wait();
        try
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_closing, this);
                if (File.Exists(_path)) File.Delete(_path);
                _index.Clear();
                _flushedRevision = _index.Revision;
                _lastError = null;
            }
        }
        finally { _writeGate.Release(); }
    }

    private async Task RunPersistenceAsync(TimeSpan interval)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(_shutdown.Token).ConfigureAwait(false))
            {
                try { await FlushAsync(_shutdown.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { break; }
                catch (Exception) { } // LastPersistenceError retains the failure; the next tick retries the dirty revision.
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _closing = true;
            return new ValueTask(_disposeTask ??= Task.Run(DisposeCoreAsync));
        }
    }

    private async Task DisposeCoreAsync()
    {
        _shutdown.Cancel();
        await _worker.ConfigureAwait(false);
        try { await FlushAsync().ConfigureAwait(false); }
        finally { _shutdown.Dispose(); }
    }

    private static ReadEntryId ParseKey(string key)
    {
        var parts = JsonSerializer.Deserialize<string[]>(key);
        return parts is { Length: 2 } && parts[0] is not null && parts[1] is not null
            ? new(parts[0], parts[1]) : throw new InvalidDataException("Invalid read progress identity.");
    }

    private sealed class ProgressData
    {
        public int Version { get; set; }
        public HashSet<string> ReadEntries { get; set; } = [];
    }
}
