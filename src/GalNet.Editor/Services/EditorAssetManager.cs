using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GalNet.Assets;
using GalNet.Assets.Provider;
using GalNet.Core.Assets;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Services.Interfaces;

namespace GalNet.Editor.Services;

/// <summary>Project-aware bridge from the editor's Assets directory to the shared asset manager contract.</summary>
public sealed class EditorAssetManager : IAssetManager
{
    private readonly IProjectService _projects;
    private readonly IAssetCatalogService _catalog;
    private AssetManager _inner = new();
    public EditorAssetManager(IProjectService projects, IAssetCatalogService catalog)
    {
        _projects = projects;
        _catalog = catalog;
        _projects.CurrentChanged += OnProjectChanged;
        _catalog.Changed += OnCatalogChanged;
        Rebuild(_projects.Current);
    }
    private void OnProjectChanged(Abstraction.Project.GalProject? project) => Rebuild(project);
    private void OnCatalogChanged() => Rebuild(_projects.Current);
    private void Rebuild(Abstraction.Project.GalProject? project)
    {
        var previous = Interlocked.Exchange(ref _inner, project is null ? new AssetManager() : new AssetManager([new LocalFileProvider(project.AssetsPath, optional: true)]));
        previous.Dispose();
    }
    public Task<IGameFile?> GetFileAsync(string assetId, CancellationToken ct = default) => _inner.GetFileAsync(assetId, ct);
    public Task<IReadOnlyList<IGameFile>> GetFilesAsync(ResourceType? type = null, CancellationToken ct = default) => _inner.GetFilesAsync(type, ct);
    public Task<T?> LoadAsync<T>(string assetId, CancellationToken ct = default) where T : class => _inner.LoadAsync<T>(assetId, ct);
    public Task<T?> LoadByPathAsync<T>(string path, CancellationToken ct = default) where T : class => _inner.LoadByPathAsync<T>(path, ct);
    public void Release(string assetId) => _inner.Release(assetId);
    public bool IsLoaded(string assetId) => _inner.IsLoaded(assetId);
    public int CachedCount => _inner.CachedCount;
    public void RegisterProvider(IAssetProvider provider) => _inner.RegisterProvider(provider);
    public void ClearCache() => _inner.ClearCache();
    public void Dispose()
    {
        _projects.CurrentChanged -= OnProjectChanged;
        _catalog.Changed -= OnCatalogChanged;
        _inner.Dispose();
    }
}
