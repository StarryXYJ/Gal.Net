using GalNet.Assets.Variants;
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
    private readonly IResourceTypeCatalog _resourceTypes;
    private ScanResult? _cachedScan;
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
    public LocalFileProvider(string assetsRoot, IResourceTypeCatalog resourceTypes, bool optional = false)
    {
        _assetsRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(assetsRoot));
        _optional = optional;
        _resourceTypes = resourceTypes ?? throw new ArgumentNullException(nameof(resourceTypes));
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
            Task<ScanResult>? scanTask = null;
            ScanResult? cached = null;
            lock (_cacheLock)
            {
                if (!_cacheDirty && _cachedScan is not null)
                {
                    cached = _cachedScan;
                }
                else
                {
                    // All callers share one directory scan. Cancellation only stops the
                    // current waiter; it must not cancel a scan that other callers need.
                    scanTask = _scanTask ??= ScanAsync(_cacheVersion);
                }
            }

            if (cached is not null)
            {
                var prepared = await CreateArchiveAsync(cached, ct);
                lock (_cacheLock)
                {
                    if (!_disposed && cached.Version == _cacheVersion) return prepared;
                }
                prepared.Dispose();
                ObjectDisposedException.ThrowIf(_disposed, this);
                continue;
            }

            try
            {
                var scan = await scanTask!.WaitAsync(ct);
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

                    _cachedScan = scan;
                    _cacheDirty = false;
                }
                // Prepare through the cached branch, which also rechecks the generation after compilation.
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

    private Task<ScanResult> ScanAsync(int version) => Task.Run(() =>
    {
        for (var ancestor = new DirectoryInfo(_assetsRoot); ancestor is not null; ancestor = ancestor.Parent)
            if (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Assets root must not traverse a directory link.");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var directories = new List<string>();
        var pending = new Stack<string>();
        pending.Push(_assetsRoot);
        while (pending.TryPop(out var directory))
        {
            var relative = Path.GetRelativePath(_assetsRoot, directory).Replace('\\', '/');
            directories.Add(relative == "." ? "" : relative);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException($"Assets path '{entry}' traverses a link.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                else if (!files.TryAdd(Path.GetRelativePath(_assetsRoot, entry).Replace('\\', '/'), entry))
                    throw new InvalidDataException($"Assets contains a case-only path collision at '{entry}'.");
            }
        }
        return new ScanResult(version, files, directories);
    });

    private async Task<IArchive> CreateArchiveAsync(ScanResult snapshot, CancellationToken ct)
    {
        var files = await LocalAssetCompiler.CompileAsync(snapshot.Files, snapshot.Directories, _resourceTypes, ct);
        return new DevArchive(_assetsRoot, files.ToList(), files.ToDictionary(file => AssetPathHelper.Normalize(file.Path), file => file.Id, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Invalidates the source index after a known author edit, without writing or repairing files.</summary>
    public void Invalidate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        MarkCacheDirty();
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
            _watcher.Error += (_, _) => MarkCacheDirty();
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
            _cachedScan = null;
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

    private sealed record ScanResult(int Version, IReadOnlyDictionary<string, string> Files, IReadOnlyCollection<string> Directories);
}
