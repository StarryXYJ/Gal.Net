using GalNet.Assets;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Assets;
using GalNet.GameHost.Content;

namespace GalNet.Storage.FileSystem;

/// <summary>Loads non-resource game content directly from a verified installed package directory.</summary>
public sealed class InstalledGameContentProvider : IGameContentProvider
{
    private readonly string _directory;
    private readonly GameContentLoadOptions _options;
    private readonly IGalleryTypeCatalog _galleryTypes;
    private readonly IResourceTypeCatalog _resourceTypes;
    private readonly Func<InstalledPackageSet>? _packages;

    public InstalledGameContentProvider(string directory, IResourceTypeCatalog resourceTypes, IGalleryTypeCatalog galleryTypes, GameContentLoadOptions? options = null)
    {
        _directory = Path.GetFullPath(directory);
        _options = options ?? new();
        ArgumentNullException.ThrowIfNull(resourceTypes);
        _resourceTypes = resourceTypes;
        _galleryTypes = galleryTypes ?? throw new ArgumentNullException(nameof(galleryTypes));
    }

    internal InstalledGameContentProvider(string directory, IResourceTypeCatalog resourceTypes, IGalleryTypeCatalog galleryTypes,
        GameContentLoadOptions? options, Func<InstalledPackageSet> packages) : this(directory, resourceTypes, galleryTypes, options) => _packages = packages;

    public Task<GameContent> LoadAsync(CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var packages = _packages?.Invoke() ?? InstalledPackageSet.Load(_directory, _resourceTypes);
        using var assets = new AssetManager(packages.CreateProviders());
        var graph = await GameGraphContentLoader.LoadResourcesAsync(assets, packages.Manifest.GraphId, cancellationToken,
            packages.InstalledStories, packages.Manifest.Stories.Select(story => story.PackageId).ToHashSet(StringComparer.Ordinal));
        graph.ContentBuildId = packages.Manifest.BuildId;
        graph.InstalledStoryPackages = packages.InstalledStories.Order(StringComparer.Ordinal).ToArray();
        var gallery = GalleryFileLoader.LoadGenerated(Path.Combine(_directory, "gallery.json"), _galleryTypes);
        var settings = GameProjectConfigurationLoader.Load(_directory, _options.Configurations);
        var localization = GameLocalizationLoader.Load(_directory, graph, _options.SelectDefaultCulture?.Invoke(settings), cancellationToken);
        return new GameContent { Graph = graph, Gallery = gallery, Configuration = settings, Localization = localization };
    }, cancellationToken);
}
