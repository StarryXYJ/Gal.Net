using System.Diagnostics;
using GalNet.Core.Assets;

namespace GalNet.Assets;

/// <summary>
/// 资源管理器 —— 统一 acquire、缓存（引用计数）和释放。
///
/// 支持两种查找方式，共享同一缓存：
///   - AcquireAsync(id)         按资源 ID（GUID）查找
///   - AcquireByPathAsync(path) 按资源路径查找（如 "bg/classroom.png"）
///
/// 同一资源的并发请求共享一次底层加载，但每个调用者仍拥有独立的等待取消和引用。
/// </summary>
public sealed class AssetManager : IAssetManager
{
    private readonly List<IAssetProvider> _providers = [];
    private readonly Dictionary<CacheKey, CacheEntry> _cache = new(CacheKeyComparer.Instance);
    private readonly Dictionary<(string TypeId, Type TargetType), object> _decoders = new();
    private readonly Dictionary<string, string> _pathToId = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly Dictionary<CacheKey, InFlightLoad> _inFlight = new(CacheKeyComparer.Instance);
    private bool _disposed;

    /// <summary>Called on the loading thread, outside the cache lock. Observers must not block;
    /// observer failures are isolated and cannot change resource fallback or completion.</summary>
    public event Action<AssetDiagnostic>? Diagnostic;

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

    public void RegisterProvider(IAssetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (_lock)
        {
            ThrowIfDisposedLocked();
            _providers.Add(provider);
        }
    }

    public void RegisterDecoder<T>(string resourceTypeId, IAssetDecoder<T> decoder) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceTypeId);
        ArgumentNullException.ThrowIfNull(decoder);
        lock (_lock)
        {
            ThrowIfDisposedLocked();
            _decoders[(resourceTypeId.Trim().ToLowerInvariant(), typeof(T))] = decoder;
        }
    }

    public async Task<IGameFile?> GetFileAsync(string assetId, CancellationToken ct = default)
    {
        lock (_lock) ThrowIfDisposedLocked();
        return string.IsNullOrWhiteSpace(assetId)
            ? null
            : (await FindInProvidersAsync(NormalizeAssetId(assetId), findById: true, ct))?.File;
    }

    public async Task<IReadOnlyList<IGameFile>> GetFilesAsync(string? typeId = null, CancellationToken ct = default)
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
            string? assetId = null;
            try
            {
                if (!provider.Exists("assets")) continue;
                archive = await provider.OpenArchiveAsync("assets", ct);
                foreach (var id in archive.AssetIds)
                {
                    assetId = id;
                    ct.ThrowIfCancellationRequested();
                    if (files.ContainsKey(id)) continue;
                    var file = archive.GetAsset(id, ct);
                    if (file is not null && (typeId is null || string.Equals(file.TypeId, typeId.Trim(), StringComparison.OrdinalIgnoreCase)))
                        files[id] = file;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Report(new(AssetLoadStage.Enumeration, ProviderFailure(exception), assetId, Provider: provider.Name, Exception: exception));
            }
            finally
            {
                archive?.Dispose();
            }
        }

        return files.Values.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<AssetHandle<T>?> AcquireAsync<T>(string assetId, CancellationToken ct = default) where T : class =>
        (await AcquireAsyncCore<T>(assetId, knownFile: null, ct)).Handle;

    private async Task<(AssetHandle<T>? Handle, bool FileFound)> AcquireAsyncCore<T>(string assetId, ResolvedAsset? knownFile, CancellationToken ct) where T : class
    {
        var normalizedId = NormalizeAssetId(assetId);
        var key = new CacheKey(normalizedId, typeof(T));
        InFlightLoad load;
        var startProducer = false;

        lock (_lock)
        {
            ThrowIfDisposedLocked();
            if (TryAcquireCacheLocked(key, out T? cached, out var cachedTypeId)) return (CreateHandle(key, cachedTypeId, cached!), true);

            if (!_inFlight.TryGetValue(key, out load!))
            {
                load = new InFlightLoad(normalizedId) { PendingWaiters = 1 };
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
            var fileFound = await load.Completion.Task.WaitAsync(ct);
            var asset = TakeLoadedReference<T>(key, load, out var typeId);
            return (asset is null ? null : CreateHandle(key, typeId!, asset), fileFound);
        }
        catch
        {
            AbandonWaiter(key, load);
            throw;
        }
    }

    public async Task<AssetHandle<T>?> AcquireByPathAsync<T>(string path, CancellationToken ct = default) where T : class
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
            var (cached, fileFound) = await AcquireAsyncCore<T>(assetId, knownFile: null, ct);
            // A read/decoder failure does not invalidate the path's resolved identity.
            if (cached is not null || fileFound) return cached;

            // A development provider may have invalidated a file after its watcher fired.
            // Do not keep retrying a stale path -> ID mapping forever.
            lock (_lock)
            {
                if (_pathToId.TryGetValue(normalizedPath, out var mapped) &&
                    StringComparer.OrdinalIgnoreCase.Equals(mapped, assetId))
                    _pathToId.Remove(normalizedPath);
            }
        }

        var gameFile = await FindInProvidersAsync(normalizedPath, findById: false, ct, typeof(T));
        if (gameFile is null) return null;

        var resolvedId = NormalizeAssetId(gameFile.File.Id);
        lock (_lock)
        {
            ThrowIfDisposedLocked();
            _pathToId[normalizedPath] = resolvedId;
        }

        // Path requests join the same ID single-flight task, so two callers cannot
        // decode the same image and leak the losing IDisposable result.
        return (await AcquireAsyncCore<T>(resolvedId, gameFile, ct)).Handle;
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

    private async Task ProduceLoadAsync<T>(CacheKey key, InFlightLoad load, ResolvedAsset? knownFile) where T : class
    {
        object? result = null;
        string? typeId = null;
        var source = knownFile;
        var stage = AssetLoadStage.Lookup;
        try
        {
            source ??= await FindInProvidersAsync(load.AssetId, findById: true, load.Cancellation.Token, typeof(T));
            if (source is not null)
            {
                var gameFile = source.File;
                typeId = gameFile.TypeId;
                stage = AssetLoadStage.Read;
                var rawData = await gameFile.ReadAllBytesAsync(load.Cancellation.Token);
                stage = AssetLoadStage.Decode;
                result = await ConvertToAsync<T>(rawData, source, load.Cancellation.Token);
            }

            CompleteLoad(key, load, result, typeId, source is not null);
            result = null; // ownership transferred to the cache, or intentionally discarded there
        }
        catch (OperationCanceledException)
        {
            CompleteCanceled(key, load);
        }
        catch (Exception exception)
        {
            var kind = stage switch
            {
                AssetLoadStage.Decode => AssetFailureKind.DecodeFailure,
                _ when exception is InvalidDataException => AssetFailureKind.InvalidData,
                AssetLoadStage.Lookup => AssetFailureKind.ProviderFailure,
                _ => AssetFailureKind.ReadFailure
            };
            ReportFailure(stage, kind, source, load.AssetId, typeof(T), exception);
            CompleteLoad(key, load, null, null, source is not null);
        }
        finally
        {
            if (result is IDisposable disposable) disposable.Dispose();
            FinalizeProducer(key, load);
        }
    }

    private async ValueTask<T?> ConvertToAsync<T>(byte[] data, ResolvedAsset source, CancellationToken ct) where T : class
    {
        ct.ThrowIfCancellationRequested();
        var file = source.File;
        if (typeof(T) == typeof(byte[])) return data as T;
        if (typeof(T) == typeof(string)) return System.Text.Encoding.UTF8.GetString(data) as T;
        if (typeof(T) == typeof(IGameFile)) return file as T;

        IAssetDecoder<T>? decoder;
        lock (_lock)
        {
            decoder = _decoders.TryGetValue((file.TypeId, typeof(T)), out var registered)
                ? registered as IAssetDecoder<T>
                : null;
        }

        if (decoder is null)
        {
            ReportFailure(AssetLoadStage.Decode, AssetFailureKind.DecoderMissing, source, file.Id, typeof(T));
            return null;
        }
        var result = await decoder.DecodeAsync(file, data, ct);
        if (result is null && !ct.IsCancellationRequested)
            ReportFailure(AssetLoadStage.Decode, AssetFailureKind.DecodeFailure, source, file.Id, typeof(T));
        return result;
    }

    private async Task<ResolvedAsset?> FindInProvidersAsync(string key, bool findById, CancellationToken ct, Type? targetType = null)
    {
        List<IAssetProvider> snapshot;
        lock (_lock) snapshot = _providers.ToList();

        var failed = false;
        foreach (var provider in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            IArchive? archive = null;
            try
            {
                if (!provider.Exists("assets")) continue;
                archive = await provider.OpenArchiveAsync("assets", ct);
                var gameFile = findById ? archive.GetAsset(key, ct) : archive.GetAssetByPath(key, ct);
                if (gameFile is not null) return new(gameFile, provider.Name);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                failed = true;
                Report(new(AssetLoadStage.Lookup, ProviderFailure(exception), findById ? key : null,
                    findById ? null : key, provider.Name, targetType, exception));
            }
            finally
            {
                archive?.Dispose();
            }
        }

        if (!failed && !ct.IsCancellationRequested)
            Report(new(AssetLoadStage.Lookup, AssetFailureKind.NotFound, findById ? key : null,
                findById ? null : key, TargetType: targetType));
        return null;
    }

    private static AssetFailureKind ProviderFailure(Exception error) =>
        error is InvalidDataException ? AssetFailureKind.InvalidData : AssetFailureKind.ProviderFailure;

    private void ReportFailure(AssetLoadStage stage, AssetFailureKind kind, ResolvedAsset? source,
        string assetId, Type targetType, Exception? error = null) =>
        Report(new(stage, kind, assetId, source?.File.Path,
            source?.File is IAssetFileSource origin ? origin.ProviderName : source?.Provider,
            targetType, error, (source?.File as IAssetFileSource)?.SourceAssetId));

    private void Report(AssetDiagnostic diagnostic)
    {
        var observers = Diagnostic;
        if (observers is null)
        {
            try { Trace.TraceWarning("Asset {0} failed ({1}): id='{2}', path='{3}', provider='{4}', target='{5}': {6}",
                diagnostic.Stage, diagnostic.Kind, diagnostic.AssetId, diagnostic.Path, diagnostic.Provider, diagnostic.TargetType, diagnostic.Exception); }
            catch (Exception) { /* Trace listeners are observers too. */ }
            return;
        }
        foreach (Action<AssetDiagnostic> observer in observers.GetInvocationList())
            try { observer(diagnostic); }
            catch (Exception) { /* Diagnostics must not interrupt shared load completion. */ }
    }

    private sealed record ResolvedAsset(IGameFile File, string Provider);

    private T? TakeLoadedReference<T>(CacheKey key, InFlightLoad load, out string? typeId) where T : class
    {
        T? result = null;
        typeId = null;
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
                typeId = entry.TypeId;
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

                if (load.Completed && _cache.TryGetValue(key, out var entry) && ReferenceEquals(entry.Owner, load))
                    assetToDispose = DetachUnusedAssetLocked(key, entry);
            }
            cancellationToDispose = TryDetachCancellationLocked(load);
        }

        cancellationToCancel?.Cancel();
        cancellationToDispose?.Dispose();
        assetToDispose?.Dispose();
    }

    private void CompleteLoad(CacheKey key, InFlightLoad load, object? result, string? typeId, bool fileFound)
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
                    _cache[key] = new CacheEntry { Data = result, Owner = load, TypeId = typeId ?? "unknown" };

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
        if (accepted) load.Completion.TrySetResult(fileFound);
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

    private bool TryAcquireCacheLocked<T>(CacheKey key, out T? result, out string typeId) where T : class
    {
        if (_cache.TryGetValue(key, out var entry) && entry.Data is T typed)
        {
            entry.RefCount++;
            result = typed;
            typeId = entry.TypeId;
            return true;
        }

        result = null;
        typeId = null!;
        return false;
    }

    private IDisposable? ReleaseCoreLocked(CacheKey key)
    {
        if (!_cache.TryGetValue(key, out var entry) || entry.RefCount <= 0) return null;
        entry.RefCount--;
        return DetachUnusedAssetLocked(key, entry);
    }

    private IDisposable? DetachUnusedAssetLocked(CacheKey key, CacheEntry entry)
    {
        // Completed loads stay alive until every waiter receives a handle or abandons delivery.
        if (entry.RefCount > 0 || entry.Owner is { PendingWaiters: > 0 }) return null;
        _cache.Remove(key);
        return entry.Data as IDisposable;
    }

    private AssetHandle<T> CreateHandle<T>(CacheKey key, string typeId, T asset) where T : class =>
        new(key.AssetId, typeId, asset, () => ReleaseHandle(key));

    private void ReleaseHandle(CacheKey key)
    {
        IDisposable? disposable;
        lock (_lock)
        {
            if (_disposed) return;
            disposable = ReleaseCoreLocked(key);
        }
        disposable?.Dispose();
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
        public string TypeId = "unknown";
        public int RefCount;
    }

    private sealed class InFlightLoad(string assetId)
    {
        public string AssetId { get; } = assetId;
        // The cache owns the result; waiters only need to know whether path identity was resolved.
        public TaskCompletionSource<bool> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenSource Cancellation { get; } = new();
        public int PendingWaiters { get; set; }
        public bool Completed { get; set; }
        public bool ProducerFinished { get; set; }
        public bool Invalidated { get; set; }
        public bool CancellationDisposed { get; set; }
    }
}
