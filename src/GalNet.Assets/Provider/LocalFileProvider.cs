using System.Text.Json;
using GalNet.Core.Assets;

namespace GalNet.Assets.Provider;

/// <summary>
/// 开发模式资源提供者 —— 直接从文件系统读取原始资源文件 + .meta 描述文件。
/// 目录结构：
///   Assets/
///     characters/alice.png
///     characters/alice.png.meta
///     bg/classroom.jpg
///     bg/classroom.jpg.meta
///     ...
/// </summary>
public sealed class LocalFileProvider : IAssetProvider, IDisposable
{
    private readonly string _assetsRoot;
    private readonly bool _optional;
    private List<IGameFile>? _cachedFiles;
    private Dictionary<string, string>? _cachedPathToId;
    private Task<ScanResult>? _scanTask;
    private FileSystemWatcher? _watcher;
    private readonly object _cacheLock = new();
    private bool _cacheDirty = true;
    private int _cacheVersion;
    private bool _disposed;

    /// <summary>
    /// 创建 LocalFileProvider。
    /// </summary>
    /// <param name="assetsRoot">Assets 目录的绝对路径</param>
    /// <param name="optional">若目录不存在是否静默返回空归档</param>
    public LocalFileProvider(string assetsRoot, bool optional = false)
    {
        _assetsRoot = assetsRoot.Replace('\\', '/').TrimEnd('/');
        _optional = optional;
    }

    public string Name => $"LocalFile({_assetsRoot})";

    public bool Exists(string archiveName) => Directory.Exists(_assetsRoot);

    public IArchive OpenArchive(string archiveName) =>
        OpenArchiveAsync(archiveName).GetAwaiter().GetResult();

    public async Task<IArchive> OpenArchiveAsync(string archiveName, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        if (!Directory.Exists(_assetsRoot))
        {
            if (_optional)
                return new DevArchive(_assetsRoot, [], []);

            throw new DirectoryNotFoundException($"Assets directory not found: {_assetsRoot}");
        }

        InitWatcher();

        while (true)
        {
            Task<ScanResult> scanTask;
            lock (_cacheLock)
            {
                if (!_cacheDirty && _cachedFiles is not null && _cachedPathToId is not null)
                {
                    // 用缓存的数据快速 new 新实例返回，避免多线程 Dispose 冲突，消除 IO 扫描
                    return new DevArchive(_assetsRoot, _cachedFiles, _cachedPathToId);
                }

                // All callers share one directory scan. Cancellation only stops the
                // current waiter; it must not cancel a scan that other callers need.
                scanTask = _scanTask ??= ScanAsync(_cacheVersion);
            }

            try
            {
                var scan = await scanTask.WaitAsync(ct);
                lock (_cacheLock)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    if (!ReferenceEquals(_scanTask, scanTask)) continue;
                    _scanTask = null;

                    // A watcher event may have arrived while the scan was in progress.
                    // Do not publish a stale index as the current cache generation.
                    if (scan.Version != _cacheVersion)
                    {
                        _cacheDirty = true;
                        continue;
                    }

                    _cachedFiles = scan.Files;
                    _cachedPathToId = scan.PathToId;
                    _cacheDirty = false;
                    return new DevArchive(_assetsRoot, _cachedFiles, _cachedPathToId);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // WaitAsync cancellation is local to this caller. Keep the shared
                // directory scan alive for other waiters.
                throw;
            }
            catch
            {
                lock (_cacheLock)
                {
                    if (ReferenceEquals(_scanTask, scanTask)) _scanTask = null;
                }
                throw;
            }
        }
    }

    private async Task<ScanResult> ScanAsync(int version)
    {
        var metaFiles = Directory.GetFiles(_assetsRoot, "*.meta", SearchOption.AllDirectories);
        var files = new List<IGameFile>();
        var pathToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var metaPath in metaFiles)
        {
            var json = await File.ReadAllTextAsync(metaPath, CancellationToken.None);
            AssetMeta? meta;
            try
            {
                meta = JsonSerializer.Deserialize<AssetMeta>(json);
            }
            catch
            {
                continue;
            }

            if (meta is null || string.IsNullOrEmpty(meta.Id) || string.IsNullOrEmpty(meta.Path))
                continue;

            // The resource file path is relative to the meta file
            var resourcePath = metaPath[..^5]; // remove ".meta"
            if (!File.Exists(resourcePath))
                continue;

            var data = await File.ReadAllBytesAsync(resourcePath, CancellationToken.None);
            var hash = CryptoHelper.HashSHA256(data);
            var gameFile = new GameFile(meta.Id, meta.Path, meta.ParseResourceType(), data, hash);
            files.Add(gameFile);
            pathToId[AssetPathHelper.Normalize(meta.Path)] = meta.Id;
        }

        return new ScanResult(version, files, pathToId);
    }

    private void InitWatcher()
    {
        if (_disposed || _watcher != null || !Directory.Exists(_assetsRoot)) return;
        try
        {
            _watcher = new FileSystemWatcher(_assetsRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName
            };
            _watcher.Changed += (_, _) => MarkCacheDirty();
            _watcher.Created += (_, _) => MarkCacheDirty();
            _watcher.Deleted += (_, _) => MarkCacheDirty();
            _watcher.Renamed += (_, _) => MarkCacheDirty();
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            // 防御权限或平台不支持
        }
    }

    private void MarkCacheDirty()
    {
        lock (_cacheLock)
        {
            if (_disposed) return;
            _cacheDirty = true;
            _cacheVersion++;
        }
    }

    public void Dispose()
    {
        FileSystemWatcher? watcher;
        lock (_cacheLock)
        {
            if (_disposed) return;
            _disposed = true;
            watcher = _watcher;
            _watcher = null;
            _cachedFiles = null;
            _cachedPathToId = null;
            _scanTask = null;
            _cacheDirty = true;
            _cacheVersion++;
        }

        watcher?.Dispose();
    }

    /// <summary>
    /// 开发模式归档 —— 基于内存文件列表的轻量 IArchive。
    /// </summary>
    private sealed class DevArchive : IArchive
    {
        private readonly string _name;
        private readonly Dictionary<string, IGameFile> _byId;
        private readonly Dictionary<string, string> _pathToId;
        private bool _disposed;

        public DevArchive(string name, List<IGameFile> files, Dictionary<string, string> pathToId)
        {
            _name = name;
            _byId = files.ToDictionary(f => f.Id, f => f, StringComparer.OrdinalIgnoreCase);
            _pathToId = new Dictionary<string, string>(pathToId, StringComparer.OrdinalIgnoreCase);
        }

        public string Name => _name;
        public IEnumerable<string> AssetIds => _byId.Keys;

        public bool Contains(string assetId) => _byId.ContainsKey(assetId);

        public IGameFile? GetAsset(string assetId)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _byId.GetValueOrDefault(assetId);
        }

        public IGameFile? GetAssetByPath(string path)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pathToId.TryGetValue(AssetPathHelper.Normalize(path), out var id))
                return GetAsset(id);
            return null;
        }

        public void Dispose()
        {
            _disposed = true;
            _byId.Clear();
            _pathToId.Clear();
        }
    }

    private sealed record ScanResult(int Version, List<IGameFile> Files, Dictionary<string, string> PathToId);
}
