using GalNet.Assets.Provider;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Assets;
using GalNet.GameHost.Content;

namespace GalNet.Storage.FileSystem;

/// <summary>Resolved game input: a development directory or a verified installed package directory.</summary>
public sealed class GameInstallation
{
    private readonly Dictionary<IResourceTypeCatalog, Lazy<InstalledPackageSet>> _packageSets = new(ReferenceEqualityComparer.Instance);
    private GameInstallation(string rootDirectory, bool isPackaged)
    {
        RootDirectory = rootDirectory;
        IsPackaged = isPackaged;
    }

    public string RootDirectory { get; }
    public bool IsPackaged { get; }

    public static async Task<GameInstallation> OpenAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            if (!string.Equals(Path.GetExtension(fullPath), ".galpak", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Game input files must use the .galpak extension.");
            var installedDirectory = await GalpakInstaller.InstallAsync(fullPath, ct: ct);
            return new GameInstallation(installedDirectory, isPackaged: true);
        }

        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException($"Game directory was not found: {fullPath}");
        return new GameInstallation(fullPath, Directory.EnumerateFiles(fullPath, "*.galnet", SearchOption.TopDirectoryOnly).Any());
    }

    public IGameContentProvider CreateContentProvider(IResourceTypeCatalog resourceTypes, IGalleryTypeCatalog galleryTypes, GameContentLoadOptions? options = null) => IsPackaged
        ? new InstalledGameContentProvider(RootDirectory, resourceTypes, galleryTypes, options, () => Packages(resourceTypes))
        : new ProjectGameContentProvider(RootDirectory, resourceTypes, galleryTypes, options);

    /// <summary>Returns the session's verified base/story PAK selection, or development resources.</summary>
    public IReadOnlyList<IAssetProvider> CreateAssetProviders(IResourceTypeCatalog resourceTypes)
    {
        ArgumentNullException.ThrowIfNull(resourceTypes);
        var assetsDirectory = Path.Combine(RootDirectory, "Assets");
        if (!IsPackaged) return [new CompiledStoryAssetProvider(RootDirectory), new LocalFileProvider(assetsDirectory, resourceTypes, optional: true)];

        return Packages(resourceTypes).CreateProviders();
    }

    internal static IReadOnlyList<IAssetProvider> CreateInstalledAssetProviders(string rootDirectory, IResourceTypeCatalog resourceTypes)
    {
        return InstalledPackageSet.Load(rootDirectory, resourceTypes).CreateProviders();
    }

    private InstalledPackageSet Packages(IResourceTypeCatalog types)
    {
        Lazy<InstalledPackageSet> value;
        lock (_packageSets)
        {
            if (!_packageSets.TryGetValue(types, out value!))
                _packageSets.Add(types, value = new(() => InstalledPackageSet.Load(RootDirectory, types)));
        }
        return value.Value;
    }
}
