using GalNet.Assets.Provider;
using GalNet.Core.Assets;
using GalNet.Core.Gallery;
using GalNet.Runtime.Content;

namespace GalNet.Storage.FileSystem;

/// <summary>Resolved game input: a development directory or a verified installed package directory.</summary>
public sealed class GameInstallation
{
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

    public IGameContentProvider CreateContentProvider(IResourceTypeCatalog resourceTypes, IGalleryTypeCatalog galleryTypes) => IsPackaged
        ? new InstalledGameContentProvider(RootDirectory, resourceTypes, galleryTypes)
        : new ProjectGameContentProvider(RootDirectory, resourceTypes, galleryTypes);

    /// <summary>Returns every resource PAK in descending relative-path priority, or the source directory.</summary>
    public IReadOnlyList<IAssetProvider> CreateAssetProviders(IResourceTypeCatalog resourceTypes)
    {
        ArgumentNullException.ThrowIfNull(resourceTypes);
        var assetsDirectory = Path.Combine(RootDirectory, "Assets");
        if (!IsPackaged) return [new LocalFileProvider(assetsDirectory, resourceTypes, optional: true)];

        var providers = new List<IAssetProvider>();
        var resourcePaksDirectory = Path.Combine(assetsDirectory, "Paks");
        if (Directory.Exists(resourcePaksDirectory))
        {
            foreach (var pakPath in Directory.EnumerateFiles(resourcePaksDirectory, "*.pak", SearchOption.AllDirectories)
                         .OrderByDescending(path => Path.GetRelativePath(resourcePaksDirectory, path), StringComparer.OrdinalIgnoreCase))
                providers.Add(new PakFileProvider(pakPath, "assets", resourceTypes));
        }
        return providers;
    }
}
