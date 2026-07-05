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
    private readonly Dictionary<string, CacheEntry> _cache = new();
    private readonly Dictionary<string, string> _pathToId = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
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

    public bool IsLoaded(string assetId)
    {
        lock (_lock)
            return _cache.ContainsKey(assetId);
    }

    // ── 按 ID 加载 ──

    public async Task<T?> LoadAsync<T>(string assetId, CancellationToken ct = default) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(assetId);

        // 检查缓存
        var cached = CheckCache<T>(assetId);
        if (cached != null) return cached;

        // 遍历 provider 按 ID 查找
        var gameFile = await FindInProvidersAsync(assetId, findById: true, ct);
        if (gameFile == null) return null;

        return await LoadAndCacheAsync<T>(assetId, gameFile, ct);
    }

    // ── 按路径加载 ──

    public async Task<T?> LoadByPathAsync<T>(string path, CancellationToken ct = default) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(path);

        var normalizedPath = NormalizePath(path);

        // 检查路径缓存（path → id）
        string? assetId;
        lock (_lock)
        {
            if (_pathToId.TryGetValue(normalizedPath, out assetId) && assetId != null)
            {
                var cached = CheckCache<T>(assetId);
                if (cached != null) return cached;
            }
        }

        // 已知道 ID 但不在缓存中？直接从缓存字典再查一次（防并发竞争）
        if (assetId != null)
        {
            var cached = CheckCache<T>(assetId);
            if (cached != null) return cached;
        }

        // 遍历 provider 按路径查找
        var gameFile = await FindInProvidersAsync(normalizedPath, findById: false, ct);
        if (gameFile == null) return null;

        // 记录 path → id 映射
        lock (_lock)
            _pathToId[normalizedPath] = gameFile.Id;

        return await LoadAndCacheAsync<T>(gameFile.Id, gameFile, ct);
    }

    // ── 释放 / 清理 ──

    public void Release(string assetId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lock)
        {
            if (!_cache.TryGetValue(assetId, out var entry))
                return;

            entry.RefCount--;
            if (entry.RefCount <= 0)
                _cache.Remove(assetId);
        }
    }

    public void ClearCache()
    {
        lock (_lock)
        {
            _cache.Clear();
            _pathToId.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lock)
        {
            _cache.Clear();
            _pathToId.Clear();
            _providers.Clear();
        }
    }

    // ── 内部实现 ──

    /// <summary>检查缓存并增加引用计数。</summary>
    private T? CheckCache<T>(string assetId) where T : class
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(assetId, out var entry))
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

        var result = ConvertTo<T>(rawData, gameFile);
        if (result == null)
            return null;

        lock (_lock)
        {
            if (_cache.TryGetValue(assetId, out var existing))
            {
                existing.RefCount++;
                return existing.Data as T;
            }

            _cache[assetId] = new CacheEntry
            {
                Data = result,
                RawData = rawData,
                GameFile = gameFile,
                RefCount = 1,
            };
        }

        return result;
    }

    /// <summary>统一路径格式（小写 / 分隔符）。</summary>
    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/').ToLowerInvariant();

    /// <summary>
    /// 将原始字节数据转换为目标类型。
    /// 当前支持：
    ///   - byte[] → byte[]
    ///   - byte[] → string (UTF-8)
    ///   扩展点：后续可注册自定义转换器（如 texture → ImageSource）
    /// </summary>
    private static T? ConvertTo<T>(byte[] data, IGameFile file) where T : class
    {
        if (typeof(T) == typeof(byte[]))
            return data as T;

        if (typeof(T) == typeof(string))
            return System.Text.Encoding.UTF8.GetString(data) as T;

        if (typeof(T) == typeof(IGameFile))
            return file as T;

        return null;
    }

    private sealed class CacheEntry
    {
        public object Data = default!;
        public byte[] RawData = [];
        public IGameFile GameFile = default!;
        public int RefCount;
    }
}
