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
    private readonly IResourceTypeCatalog _resourceTypes;
    private AssetManager _inner = new();
    public EditorAssetManager(IProjectService projects, IAssetCatalogService catalog, IResourceTypeCatalog resourceTypes)
    {
        _projects = projects;
        _catalog = catalog;
        _resourceTypes = resourceTypes;
        _projects.CurrentChanged += OnProjectChanged;
        _catalog.Changed += OnCatalogChanged;
        Rebuild(_projects.Current);
    }
    private void OnProjectChanged(Abstraction.Project.GalProject? project) => Rebuild(project);
    private void OnCatalogChanged() => Rebuild(_projects.Current);
    private void Rebuild(Abstraction.Project.GalProject? project)
    {
        var previous = Interlocked.Exchange(ref _inner, project is null ? new AssetManager() : new AssetManager([new LocalFileProvider(project.AssetsPath, _resourceTypes, optional: true)]));
        previous.Dispose();
    }
    public Task<IGameFile?> GetFileAsync(string assetId, CancellationToken ct = default) => _inner.GetFileAsync(assetId, ct);
    public Task<IReadOnlyList<IGameFile>> GetFilesAsync(string? typeId = null, CancellationToken ct = default) => _inner.GetFilesAsync(typeId, ct);
    public Task<AssetHandle<T>?> AcquireAsync<T>(string assetId, CancellationToken ct = default) where T : class => _inner.AcquireAsync<T>(assetId, ct);
    public Task<AssetHandle<T>?> AcquireByPathAsync<T>(string path, CancellationToken ct = default) where T : class => _inner.AcquireByPathAsync<T>(path, ct);
    public void RegisterDecoder<T>(string resourceTypeId, IAssetDecoder<T> decoder) where T : class => _inner.RegisterDecoder(resourceTypeId, decoder);
    public void RegisterProvider(IAssetProvider provider) => _inner.RegisterProvider(provider);
    public void Dispose()
    {
        _projects.CurrentChanged -= OnProjectChanged;
        _catalog.Changed -= OnCatalogChanged;
        _inner.Dispose();
    }
}
