using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GalNet.Assets.Provider;
using GalNet.Core.Assets;
using GalNet.Core.Gallery;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.ViewModels;
using GalNet.Runtime.Content;
using GalNet.Runtime.Loader;
using GalNet.Storage.FileSystem;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Services;

/// <summary>
/// Editor implementation of IGameContentProvider.
/// Generates preview data from the current editor workspace state.
/// </summary>
public sealed class EditorGameDataProvider : IGameContentProvider
{
    private readonly IProjectService _projectService;
    private readonly IResourceTypeCatalog _resourceTypes;
    private readonly IGalleryTypeCatalog _galleryTypes;

    public EditorGameDataProvider(IProjectService projectService, IResourceTypeCatalog resourceTypes, IGalleryTypeCatalog galleryTypes)
    {
        _projectService = projectService;
        _resourceTypes = resourceTypes;
        _galleryTypes = galleryTypes;
    }

    public Task<GameContent> LoadAsync(CancellationToken cancellationToken = default)
    {
        var project = _projectService.Current ?? throw new InvalidOperationException("A project must be open before preview data is requested.");
        var directory = project.Services.GetRequiredService<EditorWorkspaceViewModel>().BuildPreviewData();
        var graph = GraphLoader.LoadFromFile(Path.Combine(directory, "graph.json"));
        foreach (var group in graph.Nodes.OfType<GalNet.Core.Graph.Group>())
        {
            var file = Path.Combine(directory, $"{group.Id}.galgroup");
            if (File.Exists(file)) GalgroupLoader.LoadIntoGroup(group, file);
        }
        using var assets = new LocalFileProvider(project.AssetsPath, _resourceTypes, optional: true);
        using var archive = assets.OpenArchive("assets");
        var gallery = new GalleryCatalogCompiler(_resourceTypes, _galleryTypes).Compile(archive.AssetIds.Select(id => archive.GetAsset(id)!.Metadata));
        return Task.FromResult(new GameContent { Graph = graph, AssetRoot = project.AssetsPath, Gallery = gallery });
    }
}
