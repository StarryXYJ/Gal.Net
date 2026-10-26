using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GalNet.Assets;
using GalNet.Assets.Provider;
using GalNet.Core.Assets;
using GalNet.Core.Configuration;
using GalNet.Core.Entry;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Serialization;

namespace GalNet.Editor.Shared.Services;

public static class GamePackageExporter
{
    private const string AssetsPakPath = "Assets/Paks/000-base.pak";

    /// <summary>Builds a distributable content directory without creating an intermediate .galpak archive.</summary>
    public static async Task<GameContentExportResult> ExportDirectoryAsync(
        string projectId,
        string projectName,
        string projectRoot,
        string outputDirectory,
        IResourceTypeCatalog resourceTypes,
        IGalleryTypeCatalog galleryTypes,
        IEntryCatalog entryCatalog,
        ProjectConfigurationSchema? configurations = null,
        CancellationToken cancellationToken = default)
    {
        string? stagingRoot = null;
        string? publishRoot = null;
        try
        {
            ArgumentNullException.ThrowIfNull(entryCatalog);
            ArgumentNullException.ThrowIfNull(resourceTypes);
            ArgumentNullException.ThrowIfNull(galleryTypes);
            var sourceRoot = Path.GetFullPath(projectRoot);
            var targetRoot = Path.GetFullPath(outputDirectory);
            ProjectContentBuilder.ValidateOutputPath(sourceRoot, targetRoot);
            if (Directory.Exists(targetRoot) || File.Exists(targetRoot))
                throw new InvalidOperationException($"Export directory '{targetRoot}' already exists.");

            var targetParent = Path.GetDirectoryName(targetRoot)
                ?? throw new InvalidOperationException("Export directory must have a parent directory.");
            Directory.CreateDirectory(targetParent);
            publishRoot = Path.Combine(targetParent, $".{Path.GetFileName(targetRoot)}.export-{Guid.NewGuid():N}");
            stagingRoot = Path.Combine(targetParent, $".{Path.GetFileName(targetRoot)}.content-{Guid.NewGuid():N}");
            var prepared = await PrepareAsync(projectId, projectName, sourceRoot, stagingRoot,
                resourceTypes, galleryTypes, entryCatalog, configurations, cancellationToken).ConfigureAwait(false);

            Directory.CreateDirectory(publishRoot);
            foreach (var (relativePath, data) in prepared.Payloads)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = GetSafeDestination(publishRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await File.WriteAllBytesAsync(destination, data, cancellationToken).ConfigureAwait(false);
            }
            await File.WriteAllBytesAsync(GetSafeDestination(publishRoot, prepared.ManifestPath), prepared.ManifestBytes, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(Path.Combine(publishRoot, "I18n", "External"));
            await VerifyDirectoryAsync(publishRoot, prepared.Files, resourceTypes, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(publishRoot, targetRoot);
            publishRoot = null;
            return GameContentExportResult.Succeeded(targetRoot);
        }
        catch (OperationCanceledException)
        {
            return GameContentExportResult.Failed("Export canceled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException or ArgumentException)
        {
            return GameContentExportResult.Failed(ex.Message);
        }
        finally
        {
            if (stagingRoot is not null && Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true);
            if (publishRoot is not null && Directory.Exists(publishRoot)) Directory.Delete(publishRoot, recursive: true);
        }
    }

    public static async Task<GamePackageExportResult> ExportAsync(
        string projectId,
        string projectName,
        string projectRoot,
        string outputDirectory,
        IResourceTypeCatalog resourceTypes,
        IGalleryTypeCatalog galleryTypes,
        IEntryCatalog entryCatalog,
        ProjectConfigurationSchema? configurations = null,
        CancellationToken cancellationToken = default)
    {
        string? temporaryPath = null;
        string? stagingRoot = null;
        try
        {
            ArgumentNullException.ThrowIfNull(entryCatalog);
            ProjectContentBuilder.ValidateOutputPath(Path.GetFullPath(projectRoot), Path.GetFullPath(outputDirectory));
            Directory.CreateDirectory(outputDirectory);
            var packagePath = Path.Combine(outputDirectory, $"{SafeFileName(projectName)}.galpak");
            temporaryPath = packagePath + ".export-" + Guid.NewGuid().ToString("N") + ".tmp";
            ArgumentNullException.ThrowIfNull(resourceTypes);
            ArgumentNullException.ThrowIfNull(galleryTypes);
            stagingRoot = Path.Combine(Path.GetTempPath(), $"galnet-export-{Guid.NewGuid():N}");
            var prepared = await PrepareAsync(projectId, projectName, projectRoot, stagingRoot,
                resourceTypes, galleryTypes, entryCatalog, configurations, cancellationToken).ConfigureAwait(false);
            await using (var file = File.Create(temporaryPath))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var packageFile in prepared.Payloads)
                    await WriteEntryAsync(zip, packageFile.Item1, packageFile.Item2, cancellationToken);
                await WriteEntryAsync(zip, prepared.ManifestPath, prepared.ManifestBytes, cancellationToken);
            }
            await VerifyAsync(temporaryPath, prepared.Files, resourceTypes, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, packagePath, true);
            return GamePackageExportResult.Succeeded(packagePath);
        }
        catch (OperationCanceledException)
        {
            DeleteTemporary(temporaryPath);
            return GamePackageExportResult.Failed("Export canceled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException or ArgumentException)
        {
            DeleteTemporary(temporaryPath);
            return GamePackageExportResult.Failed(ex.Message);
        }
        finally
        {
            if (stagingRoot is not null && Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true);
        }
    }

    private sealed record PreparedExport((string Path, byte[] Data)[] Payloads, GalpakFileEntry[] Files, string ManifestPath, byte[] ManifestBytes);

    private static async Task<PreparedExport> PrepareAsync(string projectId, string projectName, string projectRoot,
        string stagingRoot, IResourceTypeCatalog resourceTypes, IGalleryTypeCatalog galleryTypes, IEntryCatalog entryCatalog,
        ProjectConfigurationSchema? configurations, CancellationToken ct)
    {
        var (_, prepared) = await new ProjectContentBuilder(entryCatalog, resourceTypes, configurations).BuildPreparedAsync(
            projectRoot, stagingRoot, async (stage, token) =>
            {
                var assets = await LoadAssetsAsync(Path.Combine(stage, "Assets"), resourceTypes, token).ConfigureAwait(false);
                var gallery = new GalleryCatalogCompiler(resourceTypes, galleryTypes).Compile(assets.Select(asset => asset.Metadata));
                var assetsPak = PakBuilder.Build("assets", assets, resourceTypes, GalNet.Core.Assets.CompressionMode.Brotli);
                var payloads = new[] { (Path: AssetsPakPath, Data: assetsPak) }.Concat(BuildContentFiles(stage, gallery, token)).ToArray();
                var files = payloads.Select(file => new GalpakFileEntry(file.Path, Hash(file.Data), file.Data.Length)).ToArray();
                var manifest = new GalpakManifest(1, projectId, projectName, DateTimeOffset.UtcNow, files);
                return new PreparedExport(payloads, files, $"{SafeFileName(projectName)}.galnet", JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions));
            }, ct).ConfigureAwait(false);
        return prepared;
    }

    private static async Task<IReadOnlyList<IGameFile>> LoadAssetsAsync(string assetsPath, IResourceTypeCatalog resourceTypes, CancellationToken ct)
    {
        using var provider = new LocalFileProvider(assetsPath, resourceTypes, optional: true);
        using var archive = await provider.OpenArchiveAsync("assets", ct);
        return archive.AssetIds.OrderBy(id => id, StringComparer.Ordinal).Select(id => archive.GetAsset(id)!).ToArray();
    }

    private static (string Path, byte[] Data)[] BuildContentFiles(string projectRoot, GalleryCatalog gallery, CancellationToken ct)
    {
        return Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(projectRoot, path).Replace('\\', '/'))
            .Where(path => path is "settings.json" || path.StartsWith("Graph/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("I18n/", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.StartsWith("I18n/External/", StringComparison.OrdinalIgnoreCase))
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

    private static async Task VerifyAsync(string path, IReadOnlyList<GalpakFileEntry> packages, IResourceTypeCatalog resourceTypes, CancellationToken ct)
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
                using var archive = Archive.Deserialize(Path.GetFileNameWithoutExtension(package.Path), data, resourceTypes);
            }
        }
    }

    private static async Task VerifyDirectoryAsync(string root, IReadOnlyList<GalpakFileEntry> packages, IResourceTypeCatalog resourceTypes, CancellationToken ct)
    {
        foreach (var package in packages)
        {
            ct.ThrowIfCancellationRequested();
            var path = GetSafeDestination(root, package.Path);
            if (!File.Exists(path)) throw new InvalidDataException($"Export directory is missing '{package.Path}'.");
            var data = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            if (data.LongLength != package.Size || !string.Equals(Hash(data), package.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException($"Checksum mismatch for '{package.Path}'.");
            if (package.Path.EndsWith(".pak", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = Archive.Deserialize(Path.GetFileNameWithoutExtension(package.Path), data, resourceTypes);
            }
        }
    }

    private static string GetSafeDestination(string root, string relativePath) => FileSystemPaths.ResolveRelativeFile(root, relativePath);

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

public sealed record GameContentExportResult(bool Success, string? ContentDirectory, string? Error)
{
    public static GameContentExportResult Succeeded(string path) => new(true, path, null);
    public static GameContentExportResult Failed(string error) => new(false, null, error);
}
