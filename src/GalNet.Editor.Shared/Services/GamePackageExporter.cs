using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GalNet.Assets;
using GalNet.Assets.Provider;
using GalNet.Core.Assets;
using GalNet.Core.Gallery;
using GalNet.Core.Serialization;

namespace GalNet.Editor.Shared.Services;

public static class GamePackageExporter
{
    private const string AssetsPakPath = "Assets/Paks/000-base.pak";

    public static async Task<GamePackageExportResult> ExportAsync(
        string projectId,
        string projectName,
        string projectRoot,
        string outputDirectory,
        IResourceTypeCatalog resourceTypes,
        IGalleryTypeCatalog galleryTypes,
        CancellationToken cancellationToken = default)
    {
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(outputDirectory);
            var packagePath = Path.Combine(outputDirectory, $"{SafeFileName(projectName)}.galpak");
            temporaryPath = packagePath + ".tmp";
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            ArgumentNullException.ThrowIfNull(resourceTypes);
            ArgumentNullException.ThrowIfNull(galleryTypes);
            var assets = await LoadAssetsAsync(Path.Combine(projectRoot, "Assets"), resourceTypes, cancellationToken);
            var gallery = new GalleryCatalogCompiler(resourceTypes, galleryTypes).Compile(assets.Select(asset => asset.Metadata));
            var assetsPak = PakBuilder.Build("assets", assets, GalNet.Core.Assets.CompressionMode.Brotli, resourceTypes);
            var contentFiles = BuildContentFiles(projectRoot, gallery, cancellationToken);
            var packageFiles = new[] { (AssetsPakPath, assetsPak) }.Concat(contentFiles).ToArray();
            var packages = packageFiles.Select(file => new GalpakFileEntry(file.Item1, Hash(file.Item2), file.Item2.Length)).ToArray();
            await using (var file = File.Create(temporaryPath))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var packageFile in packageFiles)
                    await WriteEntryAsync(zip, packageFile.Item1, packageFile.Item2, cancellationToken);
                var manifest = new GalpakManifest(1, projectId, projectName, DateTimeOffset.UtcNow, packages);
                await WriteEntryAsync(zip, $"{SafeFileName(projectName)}.galnet", JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions), cancellationToken);
            }
            await VerifyAsync(temporaryPath, packages, cancellationToken);
            File.Move(temporaryPath, packagePath, true);
            return GamePackageExportResult.Succeeded(packagePath);
        }
        catch (OperationCanceledException)
        {
            DeleteTemporary(temporaryPath);
            return GamePackageExportResult.Failed("Export canceled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException)
        {
            DeleteTemporary(temporaryPath);
            return GamePackageExportResult.Failed(ex.Message);
        }
    }

    private static async Task<IReadOnlyList<IGameFile>> LoadAssetsAsync(string assetsPath, IResourceTypeCatalog resourceTypes, CancellationToken ct)
    {
        using var provider = new LocalFileProvider(assetsPath, resourceTypes, optional: true);
        using var archive = await provider.OpenArchiveAsync("assets", ct);
        return archive.AssetIds.OrderBy(id => id, StringComparer.Ordinal).Select(id => archive.GetAsset(id)!).ToArray();
    }

    private static IReadOnlyList<(string Path, byte[] Data)> BuildContentFiles(string projectRoot, GalleryCatalog gallery, CancellationToken ct)
    {
        return Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(projectRoot, path).Replace('\\', '/'))
            .Where(path => path is "settings.json" || path.StartsWith("Graph/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("I18n/", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path =>
            {
                ct.ThrowIfCancellationRequested();
                return (path, File.ReadAllBytes(Path.Combine(projectRoot, path.Replace('/', Path.DirectorySeparatorChar))));
            })
            .Append(("gallery.json", JsonSerializer.SerializeToUtf8Bytes(new GalleryConfiguration { Types = gallery.Types.ToList(), Items = gallery.Items.ToList() }, JsonOptions)))
            .ToArray();
    }

    private static async Task WriteEntryAsync(ZipArchive zip, string path, byte[] data, CancellationToken ct)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(data, ct);
    }

    private static async Task VerifyAsync(string path, IReadOnlyList<GalpakFileEntry> packages, CancellationToken ct)
    {
        await using var file = File.OpenRead(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        foreach (var package in packages)
        {
            var entry = zip.GetEntry(package.Path) ?? throw new InvalidDataException($"Export package is missing '{package.Path}'.");
            await using var stream = entry.Open();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, ct);
            var data = memory.ToArray();
            if (!string.Equals(Hash(data), package.Sha256, StringComparison.Ordinal)) throw new InvalidDataException($"Checksum mismatch for '{package.Path}'.");
            if (package.Path.EndsWith(".pak", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = Archive.Deserialize(Path.GetFileNameWithoutExtension(package.Path), data);
            }
        }
    }

    private static void DeleteTemporary(string? path) { if (path is not null && File.Exists(path)) File.Delete(path); }
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    private static string SafeFileName(string name) => string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}

public sealed record GamePackageExportResult(bool Success, string? PackagePath, string? Error)
{
    public static GamePackageExportResult Succeeded(string path) => new(true, path, null);
    public static GamePackageExportResult Failed(string error) => new(false, null, error);
}
