using GalNet.Core.Assets;

namespace GalNet.Assets;

/// <summary>Materializes acquired bytes once while any consumer retains a file lease.</summary>
public sealed class AssetFileMaterializer(IAssetManager assets) : IAssetFileMaterializer
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GalNet", "Media", Guid.NewGuid().ToString("N"));
    private bool _disposed;
    private bool _drained;
    private Task? _disposeTask;

    public async Task<AssetFileLease> AcquireAsync(string assetId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        CancellationTokenSource cancellation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        }
        using (cancellation)
        {
            await _loadGate.WaitAsync(cancellation.Token).ConfigureAwait(false);
            string? path = null;
            try
            {
                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    if (_entries.TryGetValue(assetId, out var cached)) return Lease(assetId, cached);
                }
                var file = await assets.GetFileAsync(assetId, cancellation.Token).ConfigureAwait(false)
                    ?? throw new FileNotFoundException($"Asset '{assetId}' was not found.");
                using var handle = await assets.AcquireAsync<byte[]>(assetId, cancellation.Token).ConfigureAwait(false)
                    ?? throw new InvalidDataException($"Asset '{assetId}' bytes could not be acquired.");
                Directory.CreateDirectory(_directory);
                path = System.IO.Path.Combine(_directory, Guid.NewGuid().ToString("N") + System.IO.Path.GetExtension(file.Path));
                await File.WriteAllBytesAsync(path, handle.Value, cancellation.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    var entry = new Entry(path, file.TypeId);
                    _entries.Add(assetId, entry);
                    path = null;
                    return Lease(assetId, entry);
                }
            }
            finally
            {
                try { if (path is not null && File.Exists(path)) File.Delete(path); }
                finally { _loadGate.Release(); }
            }
        }
    }

    private AssetFileLease Lease(string assetId, Entry entry)
    {
        entry.References++;
        return new AssetFileLease(assetId, entry.TypeId, entry.Path, () => Release(assetId, entry));
    }

    private void Release(string assetId, Entry entry)
    {
        lock (_gate)
        {
            if (--entry.References != 0) return;
            _entries.Remove(assetId);
            File.Delete(entry.Path);
            RemoveEmptyDirectory();
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeTask is not null) return new ValueTask(_disposeTask);
            _disposed = true;
            _shutdown.Cancel();
            return new ValueTask(_disposeTask = Task.Run(DrainAsync));
        }
    }

    private async Task DrainAsync()
    {
        await _loadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                _drained = true;
                RemoveEmptyDirectory();
            }
            _shutdown.Dispose();
        }
        finally { _loadGate.Release(); }
    }

    private void RemoveEmptyDirectory()
    {
        // Disposal rejects new acquires; outstanding leases still own their files.
        if (_drained && _entries.Count == 0 && Directory.Exists(_directory)) Directory.Delete(_directory);
    }

    private sealed class Entry(string path, string typeId)
    {
        public string Path { get; } = path;
        public string TypeId { get; } = typeId;
        public int References { get; set; }
    }
}
