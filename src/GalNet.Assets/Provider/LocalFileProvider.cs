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
public sealed class LocalFileProvider : IAssetProvider
{
    private readonly string _assetsRoot;
    private readonly bool _optional;
    private List<IGameFile>? _cachedFiles;
    private Dictionary<string, string>? _cachedPathToId;
    private FileSystemWatcher? _watcher;
    private readonly object _cacheLock = new();
    private bool _cacheDirty = true;

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
        if (!Directory.Exists(_assetsRoot))
        {
            if (_optional)
                return new DevArchive(_assetsRoot, [], []);

            throw new DirectoryNotFoundException($"Assets directory not found: {_assetsRoot}");
        }

        InitWatcher();

        lock (_cacheLock)
        {
            if (!_cacheDirty && _cachedFiles != null && _cachedPathToId != null)
            {
                // 用缓存的数据快速 new 新实例返回，避免多线程 Dispose 冲突，消除 IO 扫描
                return new DevArchive(_assetsRoot, _cachedFiles, _cachedPathToId);
            }
        }

        // Scan for meta files
        var metaFiles = Directory.GetFiles(_assetsRoot, "*.meta", SearchOption.AllDirectories);
        var files = new List<IGameFile>();
        var pathToId = new Dictionary<string, string>();

        foreach (var metaPath in metaFiles)
        {
            ct.ThrowIfCancellationRequested();

            var json = await File.ReadAllTextAsync(metaPath, ct);
            AssetMeta? meta;
            try
            {
                meta = JsonSerializer.Deserialize<AssetMeta>(json);
            }
            catch
            {
                continue;
            }

            if (meta == null || string.IsNullOrEmpty(meta.Id) || string.IsNullOrEmpty(meta.Path))
                continue;

            // The resource file path is relative to the meta file
            var resourcePath = metaPath[..^5]; // remove ".meta"
            if (!File.Exists(resourcePath))
                continue;

            var data = await File.ReadAllBytesAsync(resourcePath, ct);
            var hash = CryptoHelper.HashSHA256(data);
            var gameFile = new GameFile(meta.Id, meta.Path, meta.ParseResourceType(), data, hash);
            files.Add(gameFile);
            pathToId[AssetPathHelper.Normalize(meta.Path)] = meta.Id;
        }

        lock (_cacheLock)
        {
            _cachedFiles = files;
            _cachedPathToId = pathToId;
            _cacheDirty = false;
        }

        return new DevArchive(_assetsRoot, files, pathToId);
    }

    private void InitWatcher()
    {
        if (_watcher != null || !Directory.Exists(_assetsRoot)) return;
        try
        {
            _watcher = new FileSystemWatcher(_assetsRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName
            };
            _watcher.Changed += (s, e) => { lock (_cacheLock) { _cacheDirty = true; } };
            _watcher.Created += (s, e) => { lock (_cacheLock) { _cacheDirty = true; } };
            _watcher.Deleted += (s, e) => { lock (_cacheLock) { _cacheDirty = true; } };
            _watcher.Renamed += (s, e) => { lock (_cacheLock) { _cacheDirty = true; } };
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            // 防御权限或平台不支持
        }
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
            _byId = files.ToDictionary(f => f.Id, f => f);
            _pathToId = pathToId;
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
}
