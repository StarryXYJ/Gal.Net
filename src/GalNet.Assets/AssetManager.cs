using GalNet.Core.Assets;

namespace GalNet.Assets;

/// <summary>
/// 资源管理器 —— 统一加载/缓存（引用计数）/释放。
///
/// 支持两种查找方式，共享同一缓存：
///   - LoadAsync(id)        按资源 ID（GUID）查找
///   - LoadByPathAsync(path) 按资源路径查找（如 "bg/classroom.png"）
///
/// 双模式支持：
///   - 开发模式：注册 LocalFileProvider（读取原始文件 + .meta）
///   - 打包模式：注册 PakFileProvider（从 .pak 读取）
///   两者可同时注册，优先命中先返回。
/// </summary>
public sealed class AssetManager : IAssetManager
{
    private readonly List<IAssetProvider> _providers = [];
    private readonly Dictionary<CacheKey, CacheEntry> _cache = new();
    private readonly Dictionary<Type, object> _decoders = new();
    private readonly Dictionary<string, string> _pathToId = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly Dictionary<CacheKey, Task<object?>> _inFlight = new();
    private bool _disposed;

    public AssetManager()
    {
    }

    public AssetManager(IEnumerable<IAssetProvider> providers)
    {
        _providers.AddRange(providers);
    }

    public int CachedCount
    {
        get { lock (_lock) return _cache.Count; }
    }

    public void RegisterProvider(IAssetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (_lock)
            _providers.Add(provider);
    }

    public void RegisterDecoder<T>(IAssetDecoder<T> decoder) where T : class
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_lock) _decoders[typeof(T)] = decoder;
    }

    public bool TryGetLoaded<T>(string assetId, out T asset) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_lock)
        {
            if (_cache.TryGetValue(new CacheKey(assetId, typeof(T)), out var entry) && entry.Data is T typed)
            {
                asset = typed;
                return true;
            }
        }
        asset = null!;
        return false;
    }

    public bool IsLoaded(string assetId)
    {
        lock (_lock)
            return _cache.Keys.Any(key => string.Equals(key.AssetId, assetId, StringComparison.OrdinalIgnoreCase));
    }

    public Task<IGameFile?> GetFileAsync(string assetId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return string.IsNullOrWhiteSpace(assetId) ? Task.FromResult<IGameFile?>(null) : FindInProvidersAsync(assetId, findById: true, ct);
    }

    public async Task<IReadOnlyList<IGameFile>> GetFilesAsync(ResourceType? type = null, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        List<IAssetProvider> providers;
        lock (_lock) providers = _providers.ToList();
        var files = new Dictionary<string, IGameFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            ct.ThrowIfCancellationRequested();
            if (!provider.Exists("assets")) continue;
            IArchive? archive = null;
            try
            {
                archive = await provider.OpenArchiveAsync("assets", ct);
                foreach (var id in archive.AssetIds)
                {
                    ct.ThrowIfCancellationRequested();
                    if (files.ContainsKey(id)) continue;
                    var file = archive.GetAsset(id);
                    if (file is not null && (type is null || file.Type == type)) files[id] = file;
                }
            }
            catch { /* A failed provider must not make the UI resource browser unusable. */ }
            finally { archive?.Dispose(); }
        }
        return files.Values.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // ── 按 ID 加载 ──

    public async Task<T?> LoadAsync<T>(string assetId, CancellationToken ct = default) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(assetId);

        // 1. 检查缓存
        var key = new CacheKey(assetId, typeof(T));
        var cached = CheckCache<T>(key);
        if (cached != null) return cached;

        // 2. 检查是否有 in-flight 任务
        Task<object?>? waitTask;
        lock (_lock)
        {
            _inFlight.TryGetValue(key, out waitTask);
        }

        if (waitTask != null)
        {
            await waitTask;
            return CheckCache<T>(key);
        }

        // 3. 作为发起者启动加载任务并存入 in-flight 字典中
        var tcs = new TaskCompletionSource<object?>();
        lock (_lock)
        {
            // 防并发竞争下已经有其他线程写入
            if (_inFlight.TryGetValue(key, out waitTask))
            {
                tcs.TrySetResult(null); // 释放当前无用 tcs
            }
            else
            {
                _inFlight[key] = tcs.Task;
            }
        }

        if (waitTask != null)
        {
            await waitTask;
            return CheckCache<T>(key);
        }

        try
        {
            // 遍历 provider 按 ID 查找
            var gameFile = await FindInProvidersAsync(assetId, findById: true, ct);
            if (gameFile == null)
            {
                tcs.TrySetResult(null);
                return null;
            }

            var result = await LoadAndCacheAsync<T>(assetId, gameFile, ct);
            tcs.TrySetResult(result);
            return result;
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
            throw;
        }
        finally
        {
            lock (_lock)
            {
                _inFlight.Remove(key);
            }
        }
    }

    // ── 按路径加载 ──

    public async Task<T?> LoadByPathAsync<T>(string path, CancellationToken ct = default) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(path);

        var normalizedPath = AssetPathHelper.Normalize(path);

        // 1. 尝试从 path -> id 查找
        string? assetId = null;
        lock (_lock)
        {
            _pathToId.TryGetValue(normalizedPath, out assetId);
        }

        if (assetId != null)
        {
            return await LoadAsync<T>(assetId, ct);
        }

        // 2. 没有 ID 映射，遍历 provider 查找
        var gameFile = await FindInProvidersAsync(normalizedPath, findById: false, ct);
        if (gameFile == null) return null;

        // 记录映射
        lock (_lock)
            _pathToId[normalizedPath] = gameFile.Id;

        // 3. 直接加载并缓存（gameFile 已找到，无需重复查找 provider）
        return await LoadAndCacheAsync<T>(gameFile.Id, gameFile, ct);
    }

    // ── 释放 / 清理 ──

    public void Release(string assetId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lock)
            foreach (var key in _cache.Keys.Where(key => string.Equals(key.AssetId, assetId, StringComparison.OrdinalIgnoreCase)).ToArray())
                ReleaseCore(key);
    }

    public void Release<T>(string assetId) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_lock) ReleaseCore(new CacheKey(assetId, typeof(T)));
    }

    public void ClearCache()
    {
        lock (_lock)
        {
            DisposeCachedAssets();
            _cache.Clear();
            _pathToId.Clear();
            _inFlight.Clear();
            _decoders.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lock)
        {
            DisposeCachedAssets();
            _cache.Clear();
            _pathToId.Clear();
            _inFlight.Clear();
            _providers.Clear();
        }
    }

    // ── 内部实现 ──

    /// <summary>检查缓存并增加引用计数。</summary>
    private T? CheckCache<T>(CacheKey key) where T : class
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                entry.RefCount++;
                return entry.Data as T;
            }
        }
        return null;
    }

    /// <summary>遍历所有 provider 查找资源。</summary>
    private async Task<IGameFile?> FindInProvidersAsync(string key, bool findById, CancellationToken ct)
    {
        List<IAssetProvider> snapshot;
        lock (_lock)
            snapshot = _providers.ToList();

        foreach (var provider in snapshot)
        {
            ct.ThrowIfCancellationRequested();

            IArchive? archive = null;
            try
            {
                if (!provider.Exists("assets"))
                    continue;

                archive = await provider.OpenArchiveAsync("assets", ct);
                var gameFile = findById
                    ? archive.GetAsset(key)
                    : archive.GetAssetByPath(key);

                if (gameFile != null)
                    return gameFile;
            }
            catch
            {
                // Provider failed, try next
            }
            finally
            {
                archive?.Dispose();
            }
        }

        return null;
    }

    /// <summary>读取数据、转换类型、写入缓存。</summary>
    private async Task<T?> LoadAndCacheAsync<T>(string assetId, IGameFile gameFile, CancellationToken ct) where T : class
    {
        byte[] rawData;
        try
        {
            rawData = await gameFile.ReadAllBytesAsync(ct);
        }
        catch
        {
            return null;
        }

        var result = await ConvertToAsync<T>(rawData, gameFile, ct);
        if (result == null)
            return null;

        lock (_lock)
        {
            var key = new CacheKey(assetId, typeof(T));
            if (_cache.TryGetValue(key, out var existing))
            {
                existing.RefCount++;
                return existing.Data as T;
            }

            _cache[key] = new CacheEntry
            {
                Data = result,
                RawData = rawData,
                GameFile = gameFile,
                RefCount = 1,
            };
        }

        return result;
    }

    /// <summary>
    /// 将原始字节 data 转换为目标类型。
    /// 当前支持：
    ///   - byte[] → byte[]
    ///   - byte[] → string (UTF-8)
    ///   扩展点：后续可注册自定义转换器（如 texture → ImageSource）
    /// </summary>
    private async ValueTask<T?> ConvertToAsync<T>(byte[] data, IGameFile file, CancellationToken ct) where T : class
    {
        if (typeof(T) == typeof(byte[]))
            return data as T;

        if (typeof(T) == typeof(string))
            return System.Text.Encoding.UTF8.GetString(data) as T;

        if (typeof(T) == typeof(IGameFile))
            return file as T;

        IAssetDecoder<T>? decoder;
        lock (_lock) decoder = _decoders.TryGetValue(typeof(T), out var registered) ? registered as IAssetDecoder<T> : null;
        if (decoder is not null) return await decoder.DecodeAsync(file, data, ct);

        return null;
    }

    private void ReleaseCore(CacheKey key)
    {
        if (!_cache.TryGetValue(key, out var entry)) return;
        entry.RefCount--;
        if (entry.RefCount > 0) return;
        _cache.Remove(key);
        if (entry.Data is IDisposable disposable) disposable.Dispose();
    }

    private void DisposeCachedAssets()
    {
        foreach (var entry in _cache.Values)
            if (entry.Data is IDisposable disposable) disposable.Dispose();
    }

    private readonly record struct CacheKey(string AssetId, Type Type);

    private class CacheEntry
    {
        public object Data = default!;
        public byte[] RawData = [];
        public IGameFile GameFile = default!;
        public int RefCount;
    }
}
