using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GalNet.Core.Serialization;

namespace GalNet.Storage.FileSystem;

/// <summary>Verifies and installs a distributable <c>.galpak</c> beside its ZIP container.</summary>
public static class GalpakInstaller
{
    private const int CurrentManifestVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public static async Task<string> InstallAsync(string packagePath, string? installationDirectory = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        var sourcePath = Path.GetFullPath(packagePath);
        if (!string.Equals(Path.GetExtension(sourcePath), ".galpak", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A .galpak package is required.", nameof(packagePath));
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Game package was not found.", sourcePath);

        var targetDirectory = installationDirectory is null
            ? Path.Combine(Path.GetDirectoryName(sourcePath)!, Path.GetFileNameWithoutExtension(sourcePath))
            : Path.GetFullPath(installationDirectory);

        using var source = File.OpenRead(sourcePath);
        using var zip = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: false);
        var manifestEntry = FindManifestEntry(zip);
        var manifest = await ReadManifestAsync(manifestEntry, ct);
        ValidateManifest(manifest, zip);

        if (Directory.Exists(targetDirectory))
        {
            if (await MatchesInstalledPackageAsync(targetDirectory, manifest, ct)) return targetDirectory;
            throw new InvalidOperationException($"Installation directory '{targetDirectory}' already contains a different package. Choose a new directory or remove it explicitly.");
        }
        if (File.Exists(targetDirectory)) throw new InvalidOperationException($"Installation path '{targetDirectory}' is a file.");

        var stagingDirectory = $"{targetDirectory}.installing-{Guid.NewGuid():N}";
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            foreach (var package in manifest.Files.OrderBy(entry => entry.Path, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                var sourceEntry = zip.GetEntry(package.Path) ?? throw new InvalidDataException($"Package is missing '{package.Path}'.");
                var destinationPath = GetSafeDestination(stagingDirectory, package.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                await CopyAndVerifyAsync(sourceEntry, destinationPath, package, ct);
            }

            var installedManifest = GetSafeDestination(stagingDirectory, manifestEntry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(installedManifest)!);
            await using (var manifestSource = manifestEntry.Open())
            await using (var manifestDestination = File.Create(installedManifest))
                await manifestSource.CopyToAsync(manifestDestination, ct);

            Directory.Move(stagingDirectory, targetDirectory);
            return targetDirectory;
        }
        catch
        {
            if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
            throw;
        }
    }

    private static ZipArchiveEntry FindManifestEntry(ZipArchive zip)
    {
        var manifests = zip.Entries
            .Where(entry => entry.FullName.EndsWith(".galnet", StringComparison.OrdinalIgnoreCase) && !entry.FullName.Contains('/', StringComparison.Ordinal))
            .ToArray();
        return manifests.Length == 1
            ? manifests[0]
            : throw new InvalidDataException("A .galpak must contain exactly one root .galnet manifest.");
    }

    private static async Task<GalpakManifest> ReadManifestAsync(ZipArchiveEntry entry, CancellationToken ct)
    {
        await using var stream = entry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<GalpakManifest>(stream, JsonOptions, ct);
        return manifest ?? throw new InvalidDataException("Package manifest is empty.");
    }

    private static void ValidateManifest(GalpakManifest manifest, ZipArchive zip)
    {
        if (manifest.Version != CurrentManifestVersion) throw new InvalidDataException($"Unsupported .galpak version '{manifest.Version}'.");
        if (manifest.Files is not { Count: > 0 }) throw new InvalidDataException("Package manifest has no payloads.");

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in manifest.Files)
        {
            if (package.Size < 0 || string.IsNullOrWhiteSpace(package.Sha256) || package.Sha256.Length != 64 || !package.Sha256.All(char.IsAsciiHexDigit))
                throw new InvalidDataException($"Package manifest entry '{package.Path}' has an invalid checksum or size.");
            ValidateRelativePath(package.Path);
            if (package.Path.EndsWith(".galnet", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The package manifest cannot also be a payload file.");
            if (!paths.Add(package.Path)) throw new InvalidDataException($"Package manifest repeats '{package.Path}'.");
            var zipEntry = zip.GetEntry(package.Path) ?? throw new InvalidDataException($"Package is missing '{package.Path}'.");
            if (zipEntry.Length != package.Size) throw new InvalidDataException($"Package entry '{package.Path}' size does not match its manifest.");
        }
    }

    private static async Task<bool> MatchesInstalledPackageAsync(string directory, GalpakManifest manifest, CancellationToken ct)
    {
        foreach (var package in manifest.Files)
        {
            var path = GetSafeDestination(directory, package.Path);
            if (!File.Exists(path) || new FileInfo(path).Length != package.Size) return false;
            await using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
            if (!string.Equals(hash, package.Sha256, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static async Task CopyAndVerifyAsync(ZipArchiveEntry source, string destination, GalpakFileEntry package, CancellationToken ct)
    {
        await using var input = source.Open();
        await using var output = File.Create(destination);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, ct);
            if (read == 0) break;
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            total += read;
        }
        var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (total != package.Size || !string.Equals(actual, package.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException($"Package entry '{package.Path}' checksum does not match its manifest.");
    }

    private static string GetSafeDestination(string root, string relativePath)
    {
        ValidateRelativePath(relativePath);
        var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!destination.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Package entry '{relativePath}' escapes its installation directory.");
        return destination;
    }

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException($"Package entry path '{path}' is not a safe relative ZIP path.");
    }
}
