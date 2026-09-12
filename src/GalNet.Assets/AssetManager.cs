using System.Diagnostics;
using GalNet.Core.Assets;

namespace GalNet.Assets;

/// <summary>
/// 资源管理器 —— 统一加载、缓存（引用计数）和释放。
///
/// 支持两种查找方式，共享同一缓存：
///   - LoadAsync(id)         按资源 ID（GUID）查找
///   - LoadByPathAsync(path) 按资源路径查找（如 "bg/classroom.png"）
///
/// 同一资源的并发请求共享一次底层加载，但每个调用者仍拥有独立的等待取消和引用。
/// </summary>
public sealed class AssetManager : IAssetManager
{
    private readonly List<IAssetProvider> _providers = [];
    private readonly Dictionary<CacheKey, CacheEntry> _cache = new(CacheKeyComparer.Instance);
    private readonly Dictionary<Type, object> _decoders = new();
    private readonly Dictionary<string, string> _pathToId = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly Dictionary<CacheKey, InFlightLoad> _inFlight = new(CacheKeyComparer.Instance);
    private bool _disposed;

    public AssetManager()
    {
    }

    public AssetManager(IEnumerable<IAssetProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            _providers.Add(provider);
        }
    }

    public int CachedCount
    {
        get { lock (_lock) return _cache.Count; }
    }

    public void RegisterProvider(IAssetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (_lock)
        {
            ThrowIfDisposedLocked();
            _providers.Add(provider);
        }
    }

    public void RegisterDecoder<T>(IAssetDecoder<T> decoder) where T : class
    {
        ArgumentNullException.ThrowIfNull(decoder);
        lock (_lock)
        {
            ThrowIfDisposedLocked();
            _decoders[typeof(T)] = decoder;
        }
    }

    public bool TryGetLoaded<T>(string assetId, out T asset) where T : class
    {
        if (string.IsNullOrWhiteSpace(assetId))
        {
            asset = null!;
            return false;
        }

        var key = new CacheKey(NormalizeAssetId(assetId), typeof(T));
        lock (_lock)
        {
            ThrowIfDisposedLocked();
            if (_cache.TryGetValue(key, out var entry) && entry.Data is T typed)
            {
                // This is a non-owning peek. It deliberately does not increment RefCount.
                asset = typed;
                return true;
            }
        }

        asset = null!;
        return false;
    }

    public bool IsLoaded(string assetId)
    {
        if (string.IsNullOrWhiteSpace(assetId)) return false;
        var normalizedId = NormalizeAssetId(assetId);
        lock (_lock)
        {
            ThrowIfDisposedLocked();
            return _cache.Keys.Any(key => StringComparer.OrdinalIgnoreCase.Equals(key.AssetId, normalizedId));
        }
    }

    public Task<IGameFile?> GetFileAsync(string assetId, CancellationToken ct = default)
    {
        lock (_lock) ThrowIfDisposedLocked();
        return string.IsNullOrWhiteSpace(assetId)
            ? Task.FromResult<IGameFile?>(null)
            : FindInProvidersAsync(NormalizeAssetId(assetId), findById: true, ct);
    }

    public async Task<IReadOnlyList<IGameFile>> GetFilesAsync(ResourceType? type = null, CancellationToken ct = default)
    {
        List<IAssetProvider> providers;
        lock (_lock)
        {
            ThrowIfDisposedLocked();
            providers = _providers.ToList();
        }

        var files = new Dictionary<string, IGameFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            ct.ThrowIfCancellationRequested();
            IArchive? archive = null;
            try
            {
                if (!provider.Exists("assets")) continue;
                archive = await provider.OpenArchiveAsync("assets", ct);
                foreach (var id in archive.AssetIds)
                {
                    ct.ThrowIfCancellationRequested();
                    if (files.ContainsKey(id)) continue;
                    var file = archive.GetAsset(id);
                    if (file is not null && (type is null || file.Type == type))
                        files[id] = file;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Trace.TraceWarning("Asset enumeration failed in provider '{0}': {1}", provider.Name, exception);
            }
            finally
            {
                archive?.Dispose();
            }
        }

        return files.Values.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public Task<T?> LoadAsync<T>(string assetId, CancellationToken ct = default) where T : class =>
        LoadAsyncCore<T>(assetId, knownFile: null, ct);

    public Task<T?> LoadAsync<T>(IGameFile file, CancellationToken ct = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(file);
        return LoadAsyncCore<T>(file.Id, file, ct);
    }

    private async Task<T?> LoadAsyncCore<T>(string assetId, IGameFile? knownFile, CancellationToken ct) where T : class
    {
        var normalizedId = NormalizeAssetId(assetId);
        var key = new CacheKey(normalizedId, typeof(T));
        InFlightLoad load;
        var startProducer = false;

        lock (_lock)
        {
            ThrowIfDisposedLocked();
            if (TryAcquireCacheLocked(key, out T? cached)) return cached;

            if (!_inFlight.TryGetValue(key, out load!))
            {
                load = new InFlightLoad(normalizedId, typeof(T)) { PendingWaiters = 1 };
                _inFlight.Add(key, load);
                startProducer = true;
            }
            else
            {
                load.PendingWaiters++;
            }
        }

        if (startProducer)
            _ = ProduceLoadAsync<T>(key, load, knownFile);

        try
        {
            // The producer is shared, but cancellation remains request-local.
            await load.Completion.Task.WaitAsync(ct);
            return TakeLoadedReference<T>(key, load);
        }
        catch
        {
            AbandonWaiter(key, load);
            throw;
        }
    }

    public async Task<T?> LoadByPathAsync<T>(string path, CancellationToken ct = default) where T : class
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var normalizedPath = AssetPathHelper.Normalize(path);

        string? assetId;
        lock (_lock)
        {
            ThrowIfDisposedLocked();
            _pathToId.TryGetValue(normalizedPath, out assetId);
        }

        if (assetId is not null)
        {
            var cached = await LoadAsync<T>(assetId, ct);
            if (cached is not null) return cached;

            // A development provider may have invalidated a file after its watcher fired.
            // Do not keep retrying a stale path -> ID mapping forever.
            lock (_lock)
            {
                if (_pathToId.TryGetValue(normalizedPath, out var mapped) &&
                    StringComparer.OrdinalIgnoreCase.Equals(mapped, assetId))
                    _pathToId.Remove(normalizedPath);
            }
        }

        var gameFile = await FindInProvidersAsync(normalizedPath, findById: false, ct);
        if (gameFile is null) return null;

        var resolvedId = NormalizeAssetId(gameFile.Id);
        lock (_lock)
        {
            ThrowIfDisposedLocked();
            _pathToId[normalizedPath] = resolvedId;
        }

        // Path requests join the same ID single-flight task, so two callers cannot
        // decode the same image and leak the losing IDisposable result.
        return await LoadAsyncCore<T>(resolvedId, gameFile, ct);
    }

    public void Release(string assetId)
    {
        if (string.IsNullOrWhiteSpace(assetId)) return;
        var normalizedId = NormalizeAssetId(assetId);
        List<IDisposable> disposables = [];
        lock (_lock)
        {
            ThrowIfDisposedLocked();
            foreach (var key in _cache.Keys
                         .Where(key => StringComparer.OrdinalIgnoreCase.Equals(key.AssetId, normalizedId))
                         .ToArray())
            {
                if (ReleaseCoreLocked(key) is { } disposable)
                    disposables.Add(disposable);
            }
        }
        DisposeAssets(disposables);
    }

    public void Release<T>(string assetId) where T : class
    {
        if (string.IsNullOrWhiteSpace(assetId)) return;
        var key = new CacheKey(NormalizeAssetId(assetId), typeof(T));
        IDisposable? disposable;
        lock (_lock)
        {
            ThrowIfDisposedLocked();
            disposable = ReleaseCoreLocked(key);
        }
        disposable?.Dispose();
    }

    public void ClearCache()
    {
        List<IDisposable> disposables;
        InFlightLoad[] loads;
        lock (_lock)
        {
            ThrowIfDisposedLocked();
            disposables = DetachCachedAssetsLocked();
            _pathToId.Clear();
            loads = _inFlight.Values.Distinct().ToArray();
            _inFlight.Clear();
            foreach (var load in loads) load.Invalidated = true;
        }

        CancelLoads(loads);
        DisposeAssets(disposables);
    }

    public void Dispose()
    {
        List<IDisposable> disposables;
        List<IDisposable> providers;
        InFlightLoad[] loads;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            disposables = DetachCachedAssetsLocked();
            _pathToId.Clear();
            loads = _inFlight.Values.Distinct().ToArray();
            _inFlight.Clear();
            foreach (var load in loads) load.Invalidated = true;
            providers = _providers.OfType<IDisposable>().Distinct().ToList();
            _providers.Clear();
            _decoders.Clear();
        }

        CancelLoads(loads);
        DisposeAssets(disposables);
        DisposeAssets(providers);
    }

    private async Task ProduceLoadAsync<T>(CacheKey key, InFlightLoad load, IGameFile? knownFile) where T : class
    {
        object? result = null;
        try
        {
            var gameFile = knownFile ?? await FindInProvidersAsync(load.AssetId, findById: true, load.Cancellation.Token);
            if (gameFile is not null)
            {
                var rawData = await gameFile.ReadAllBytesAsync(load.Cancellation.Token);
                result = await ConvertToAsync<T>(rawData, gameFile, load.Cancellation.Token);
            }

            CompleteLoad(key, load, result);
            result = null; // ownership transferred to the cache, or intentionally discarded there
        }
        catch (OperationCanceledException)
        {
            CompleteCanceled(key, load);
        }
        catch (Exception exception)
        {
            // A missing/invalid asset is a recoverable presentation failure. Keep the
            // fallback path usable, but leave a diagnostic with the provider-independent key.
            Trace.TraceWarning("Asset load failed for '{0}' as '{1}': {2}", load.AssetId, load.Type, exception);
            CompleteLoad(key, load, null);
        }
        finally
        {
            if (result is IDisposable disposable) disposable.Dispose();
            FinalizeProducer(key, load);
        }
    }

    private async ValueTask<T?> ConvertToAsync<T>(byte[] data, IGameFile file, CancellationToken ct) where T : class
    {
        ct.ThrowIfCancellationRequested();
        if (typeof(T) == typeof(byte[])) return data as T;
        if (typeof(T) == typeof(string)) return System.Text.Encoding.UTF8.GetString(data) as T;
        if (typeof(T) == typeof(IGameFile)) return file as T;

        IAssetDecoder<T>? decoder;
        lock (_lock)
        {
            decoder = _decoders.TryGetValue(typeof(T), out var registered)
                ? registered as IAssetDecoder<T>
                : null;
        }

        return decoder is null ? null : await decoder.DecodeAsync(file, data, ct);
    }

    private async Task<IGameFile?> FindInProvidersAsync(string key, bool findById, CancellationToken ct)
    {
        List<IAssetProvider> snapshot;
        lock (_lock) snapshot = _providers.ToList();

        foreach (var provider in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            IArchive? archive = null;
            try
            {
                if (!provider.Exists("assets")) continue;
                archive = await provider.OpenArchiveAsync("assets", ct);
                var gameFile = findById ? archive.GetAsset(key) : archive.GetAssetByPath(key);
                if (gameFile is not null) return gameFile;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Trace.TraceWarning("Asset lookup failed in provider '{0}' for '{1}': {2}", provider.Name, key, exception);
            }
            finally
            {
                archive?.Dispose();
            }
        }

        return null;
    }

    private T? TakeLoadedReference<T>(CacheKey key, InFlightLoad load) where T : class
    {
        T? result = null;
        CancellationTokenSource? cancellationToDispose;
        var invalidated = false;
        lock (_lock)
        {
            invalidated = load.Invalidated || _disposed;
            if (load.PendingWaiters > 0) load.PendingWaiters--;
            if (!invalidated && _cache.TryGetValue(key, out var entry) && entry.Data is T typed)
            {
                entry.RefCount++;
                result = typed;
            }

            if (load.PendingWaiters == 0 && _inFlight.TryGetValue(key, out var current) && ReferenceEquals(current, load))
                _inFlight.Remove(key);
            cancellationToDispose = TryDetachCancellationLocked(load);
        }

        cancellationToDispose?.Dispose();
        if (invalidated)
        {
            lock (_lock)
            {
                if (_disposed) ObjectDisposedException.ThrowIf(_disposed, this);
            }
            throw new OperationCanceledException("The asset load was invalidated by a cache reset.");
        }
        return result;
    }

    private void AbandonWaiter(CacheKey key, InFlightLoad load)
    {
        CancellationTokenSource? cancellationToCancel = null;
        CancellationTokenSource? cancellationToDispose;
        IDisposable? assetToDispose = null;
        lock (_lock)
        {
            if (load.PendingWaiters > 0) load.PendingWaiters--;
            if (load.PendingWaiters == 0)
            {
                if (!load.Completed && !load.Invalidated && _inFlight.TryGetValue(key, out var current) && ReferenceEquals(current, load))
                {
                    load.Invalidated = true;
                    _inFlight.Remove(key);
                    cancellationToCancel = load.Cancellation;
                }
                else if (_inFlight.TryGetValue(key, out var completed) && ReferenceEquals(completed, load))
                {
                    _inFlight.Remove(key);
                }

                if (load.Completed && _cache.TryGetValue(key, out var entry) &&
                    ReferenceEquals(entry.Owner, load) && entry.RefCount == 0)
                {
                    _cache.Remove(key);
                    assetToDispose = entry.Data as IDisposable;
                }
            }
            cancellationToDispose = TryDetachCancellationLocked(load);
        }

        cancellationToCancel?.Cancel();
        cancellationToDispose?.Dispose();
        assetToDispose?.Dispose();
    }

    private void CompleteLoad(CacheKey key, InFlightLoad load, object? result)
    {
        var accepted = false;
        object? resultToDispose = null;
        lock (_lock)
        {
            accepted = !_disposed && !load.Invalidated && load.PendingWaiters > 0 &&
                       _inFlight.TryGetValue(key, out var current) && ReferenceEquals(current, load);
            if (accepted)
            {
                if (_cache.ContainsKey(key))
                    resultToDispose = result;
                else if (result is not null)
                    _cache[key] = new CacheEntry { Data = result, Owner = load };

                load.Completed = true;
            }
            else
            {
                load.Completed = true;
                load.Invalidated = true;
                RemoveInFlightIfCurrentLocked(key, load);
                resultToDispose = result;
            }
        }

        if (resultToDispose is IDisposable disposable) disposable.Dispose();
        if (accepted) load.Completion.TrySetResult(result);
        else load.Completion.TrySetCanceled();
    }

    private void CompleteCanceled(CacheKey key, InFlightLoad load)
    {
        lock (_lock)
        {
            load.Completed = true;
            RemoveInFlightIfCurrentLocked(key, load);
        }
        load.Completion.TrySetCanceled();
    }

    private void FinalizeProducer(CacheKey key, InFlightLoad load)
    {
        CancellationTokenSource? cancellationToDispose;
        lock (_lock)
        {
            load.ProducerFinished = true;
            if (load.PendingWaiters == 0 && _inFlight.TryGetValue(key, out var current) && ReferenceEquals(current, load))
                _inFlight.Remove(key);
            cancellationToDispose = TryDetachCancellationLocked(load);
        }
        cancellationToDispose?.Dispose();
    }

    private static CancellationTokenSource? TryDetachCancellationLocked(InFlightLoad load)
    {
        if (!load.ProducerFinished || load.PendingWaiters != 0 || load.CancellationDisposed) return null;
        load.CancellationDisposed = true;
        return load.Cancellation;
    }

    private void RemoveInFlightIfCurrentLocked(CacheKey key, InFlightLoad load)
    {
        if (_inFlight.TryGetValue(key, out var current) && ReferenceEquals(current, load))
            _inFlight.Remove(key);
    }

    private bool TryAcquireCacheLocked<T>(CacheKey key, out T? result) where T : class
    {
        if (_cache.TryGetValue(key, out var entry) && entry.Data is T typed)
        {
            entry.RefCount++;
            result = typed;
            return true;
        }

        result = null;
        return false;
    }

    private IDisposable? ReleaseCoreLocked(CacheKey key)
    {
        if (!_cache.TryGetValue(key, out var entry) || entry.RefCount <= 0) return null;
        entry.RefCount--;
        if (entry.RefCount > 0) return null;
        _cache.Remove(key);
        return entry.Data as IDisposable;
    }

    private List<IDisposable> DetachCachedAssetsLocked()
    {
        var disposables = _cache.Values
            .Select(entry => entry.Data)
            .OfType<IDisposable>()
            .ToList();
        _cache.Clear();
        return disposables;
    }

    private static void CancelLoads(IEnumerable<InFlightLoad> loads)
    {
        foreach (var load in loads)
        {
            try { load.Cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
            load.Completion.TrySetCanceled();
        }
    }

    private static void DisposeAssets(IEnumerable<IDisposable> assets)
    {
        foreach (var asset in assets)
        {
            try { asset.Dispose(); }
            catch (Exception exception) { Trace.TraceWarning("Cached asset disposal failed: {0}", exception); }
        }
    }

    private void ThrowIfDisposedLocked() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static string NormalizeAssetId(string assetId)
    {
        if (string.IsNullOrWhiteSpace(assetId))
            throw new ArgumentException("Asset ID cannot be empty or whitespace.", nameof(assetId));
        return assetId.Trim();
    }

    private readonly record struct CacheKey(string AssetId, Type Type);

    private sealed class CacheKeyComparer : IEqualityComparer<CacheKey>
    {
        public static CacheKeyComparer Instance { get; } = new();

        public bool Equals(CacheKey x, CacheKey y) =>
            x.Type == y.Type && StringComparer.OrdinalIgnoreCase.Equals(x.AssetId, y.AssetId);

        public int GetHashCode(CacheKey obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.AssetId), obj.Type);
    }

    private sealed class CacheEntry
    {
        public object Data = default!;
        public InFlightLoad? Owner;
        public int RefCount;
    }

    private sealed class InFlightLoad(string assetId, Type type)
    {
        public string AssetId { get; } = assetId;
        public Type Type { get; } = type;
        public TaskCompletionSource<object?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenSource Cancellation { get; } = new();
        public int PendingWaiters { get; set; }
        public bool Completed { get; set; }
        public bool ProducerFinished { get; set; }
        public bool Invalidated { get; set; }
        public bool CancellationDisposed { get; set; }
    }
}
