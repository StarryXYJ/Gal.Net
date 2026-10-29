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
        var documents = packages.GamePackage?.Documents;
        var gallery = documents is null ? GalleryFileLoader.LoadGenerated(Path.Combine(_directory, "gallery.json"), _galleryTypes)
            : GalleryFileLoader.LoadGeneratedJson(documents.GetText("gallery.json"), _galleryTypes, "game.pak/gallery.json");
        var settings = documents is null ? GameProjectConfigurationLoader.Load(_directory, _options.Configurations)
            : GameProjectConfigurationLoader.LoadJson(documents.GetText("settings.json"), _options.Configurations, "game.pak/settings.json");
        var culture = _options.SelectDefaultCulture?.Invoke(settings);
        var localization = documents is null ? GameLocalizationLoader.Load(_directory, graph, culture, cancellationToken)
            : GameLocalizationLoader.LoadPacked(_directory, graph, documents, packages.Localization!, culture, cancellationToken);
        return new GameContent { Graph = graph, Gallery = gallery, Configuration = settings, Localization = localization };
    }, cancellationToken);
}
