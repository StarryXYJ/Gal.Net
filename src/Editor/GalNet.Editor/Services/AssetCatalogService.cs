using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GalNet.Core.Assets;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Models;
using GalNet.Editor.Services.Interfaces;

namespace GalNet.Editor.Services;

/// <summary>Read-only Assets index; mutations belong to project file history.</summary>
public sealed class AssetCatalogService : IAssetCatalogService
{
    private readonly IProjectService _projects;
    private readonly IResourceTypeCatalog _resourceTypes;
    private readonly AssetMetaCodec _metaCodec;
    private readonly Action<GalNet.Editor.Abstraction.Project.GalProject?> _projectChangedHandler;
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _watchDebounce;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public event Action? Changed;
    public AssetCatalogService(IProjectService projects, IResourceTypeCatalog resourceTypes)
    {
        _projects = projects;
        _resourceTypes = resourceTypes;
        _metaCodec = new AssetMetaCodec(_resourceTypes);
        _projectChangedHandler = _ => StartWatching();
        _projects.CurrentChanged += _projectChangedHandler;
        StartWatching();
    }

    public IReadOnlyList<AssetEntry> GetDirectory(string relativeDirectory)
    {
        var root = RootOrNull(); if (root is null) return [];
        var directory = Resolve(relativeDirectory); if (!Directory.Exists(directory)) return [];
        var entries = new List<AssetEntry>();
        foreach (var path in Directory.EnumerateDirectories(directory)) entries.Add(CreateDirectory(path));
        foreach (var path in Directory.EnumerateFiles(directory).Where(p => !p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))) entries.Add(CreateFile(path));
        return entries.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public AssetEntry? GetEntry(string relativePath)
    {
        var full = Resolve(relativePath);
        return Directory.Exists(full) ? CreateDirectory(full) : File.Exists(full) ? CreateFile(full) : null;
    }

    public bool DirectoryExists(string relativeDirectory)
    {
        try { return Directory.Exists(Resolve(relativeDirectory)); }
        catch { return false; }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            var project = _projects.Current; if (project is null) return;
            IProjectContentSession? content;
            try { content = project.Services.GetService(typeof(IProjectContentSession)) as IProjectContentSession; }
            catch (ObjectDisposedException) { return; }
            while (ReferenceEquals(_projects.Current, project) && (content is { IsPublishing: true } || Shared.Services.ExternalProjectSnapshotReader.HasPendingTransaction(project.RootPath)))
                await Task.Delay(25, cancellationToken);
            if (!ReferenceEquals(_projects.Current, project)) return;
            Changed?.Invoke();
        }
        finally { _refreshGate.Release(); }
    }

    private void StartWatching()
    {
        _watcher?.Dispose(); _watcher = null;
        var root = RootOrNull();
        if (root is null || !Directory.Exists(root)) { Changed?.Invoke(); return; }
        _watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size, EnableRaisingEvents = true };
        _watcher.Changed += OnFileSystemChanged;
        _watcher.Created += OnFileSystemChanged;
        _watcher.Deleted += OnFileSystemChanged;
        _watcher.Renamed += OnFileSystemChanged;
        _ = RefreshAsync();
    }

    private void OnFileSystemChanged(object? sender, FileSystemEventArgs args)
    {
        _watchDebounce?.Cancel(); _watchDebounce?.Dispose();
        var cts = _watchDebounce = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(250, cts.Token); await RefreshAsync(cts.Token); }
            catch (OperationCanceledException) { }
        });
    }

    public Task<string?> GetThumbnailPathAsync(AssetEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry.IsImage) return Task.FromResult<string?>(entry.FullPath);
        // Video intentionally remains an icon-only asset. Decoding a frame while browsing
        // must not create a native VLC output window or compete with the editor preview.
        return Task.FromResult<string?>(null);
    }

    private AssetEntry CreateDirectory(string fullPath) => new() { FullPath = fullPath, RelativePath = Relative(fullPath), Name = Path.GetFileName(fullPath), IsDirectory = true };
    private AssetEntry CreateFile(string fullPath)
    {
        var meta = ReadMeta(fullPath);
        return new() { FullPath = fullPath, RelativePath = Relative(fullPath), Name = Path.GetFileName(fullPath), Type = meta?.TypeId ?? InferType(fullPath), Id = meta?.Id, Filter = (meta as SpriteAssetMeta)?.Filter, Compress = meta?.Compress, HasValidMeta = meta is not null };
    }
    private AssetMeta? ReadMeta(string file)
    {
        try
        {
            var metaPath = file + ".meta";
            return File.Exists(metaPath) ? _metaCodec.Deserialize(File.ReadAllText(metaPath), Relative(file)) : null;
        }
        catch { return null; }
    }
    private string? RootOrNull() => _projects.Current?.AssetsPath;
    private string Resolve(string relative)
    {
        var root = RootOrNull() ?? throw new InvalidOperationException("No project open."); var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !string.Equals(full, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Asset path escapes Assets."); return full;
    }
    private string Relative(string full) => Path.GetRelativePath(RootOrNull()!, full).Replace('\\', '/');
    private string InferType(string path) => _resourceTypes.TryGetByExtension(Path.GetExtension(path), out var registration) ? registration.TypeId : "data";
    public void Dispose()
    {
        _projects.CurrentChanged -= _projectChangedHandler;
        _watchDebounce?.Cancel(); _watchDebounce?.Dispose(); _watcher?.Dispose();
        _refreshGate.Dispose();
    }
}
