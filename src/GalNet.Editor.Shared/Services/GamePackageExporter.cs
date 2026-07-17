using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GalNet.Assets;
using GalNet.Assets.Provider;
using GalNet.Core.Assets;

namespace GalNet.Editor.Shared.Services;

public static class GamePackageExporter
{
    private const string AssetsPakPath = "Assets/assets.pak";
    private const string ContentPakPath = "Assets/content.pak";

    public static async Task<GamePackageExportResult> ExportAsync(string projectId, string projectName, string projectRoot, string outputDirectory, CancellationToken cancellationToken = default)
    {
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(outputDirectory);
            var packagePath = Path.Combine(outputDirectory, $"{SafeFileName(projectName)}.galpak");
            temporaryPath = packagePath + ".tmp";
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            var assetsPak = await BuildAssetsAsync(Path.Combine(projectRoot, "Assets"), cancellationToken);
            var contentPak = BuildContent(projectRoot, cancellationToken);
            var packages = new[] { new PackageEntry(AssetsPakPath, Hash(assetsPak), assetsPak.Length), new PackageEntry(ContentPakPath, Hash(contentPak), contentPak.Length) };
            await using (var file = File.Create(temporaryPath))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
            {
                await WriteEntryAsync(zip, AssetsPakPath, assetsPak, cancellationToken);
                await WriteEntryAsync(zip, ContentPakPath, contentPak, cancellationToken);
                var manifest = new GalpakManifest(1, projectId, projectName, DateTimeOffset.UtcNow, ContentPakPath, packages);
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

    private static async Task<byte[]> BuildAssetsAsync(string assetsPath, CancellationToken ct)
    {
        var provider = new LocalFileProvider(assetsPath, optional: true);
        using var archive = await provider.OpenArchiveAsync("assets", ct);
        var files = archive.AssetIds.OrderBy(id => id, StringComparer.Ordinal).Select(id => archive.GetAsset(id)!).ToArray();
        return PakBuilder.Build("assets", files, GalNet.Core.Assets.CompressionMode.Brotli);
    }

    private static byte[] BuildContent(string projectRoot, CancellationToken ct)
    {
        var files = Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(projectRoot, path).Replace('\\', '/'))
            .Where(path => path is "settings.json" || path.StartsWith("Graph/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("UI/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("I18n/", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path =>
            {
                ct.ThrowIfCancellationRequested();
                return (IGameFile)new GameFile(DeterministicId(path), path, ResourceType.Unknown, File.ReadAllBytes(Path.Combine(projectRoot, path.Replace('/', Path.DirectorySeparatorChar))));
            }).ToArray();
        return PakBuilder.Build("content", files, GalNet.Core.Assets.CompressionMode.Brotli);
    }

    private static async Task WriteEntryAsync(ZipArchive zip, string path, byte[] data, CancellationToken ct)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(data, ct);
    }

    private static async Task VerifyAsync(string path, IReadOnlyList<PackageEntry> packages, CancellationToken ct)
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
            using var archive = Archive.Deserialize(Path.GetFileNameWithoutExtension(package.Path), data);
        }
    }

    private static void DeleteTemporary(string? path) { if (path is not null && File.Exists(path)) File.Delete(path); }
    private static string DeterministicId(string path) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))).ToLowerInvariant();
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    private static string SafeFileName(string name) => string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private sealed record PackageEntry(string Path, string Sha256, long Size);
    private sealed record GalpakManifest(int Version, string ProjectId, string ProjectName, DateTimeOffset ExportedAt, string EntryPackage, IReadOnlyList<PackageEntry> Packages);
}

public sealed record GamePackageExportResult(bool Success, string? PackagePath, string? Error)
{
    public static GamePackageExportResult Succeeded(string path) => new(true, path, null);
    public static GamePackageExportResult Failed(string error) => new(false, null, error);
}
