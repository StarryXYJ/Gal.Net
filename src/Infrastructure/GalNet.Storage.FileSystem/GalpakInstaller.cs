using System.IO.Compression;
using System.Security.Cryptography;
using GalNet.Assets;
using GalNet.Core.Serialization;

namespace GalNet.Storage.FileSystem;

/// <summary>Verifies and installs a distributable <c>.galpak</c> beside its ZIP container.</summary>
public static class GalpakInstaller
{
    public static async Task<string> InstallAsync(string packagePath, string? installationDirectory = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        var sourcePath = Path.GetFullPath(packagePath);
        if (!string.Equals(Path.GetExtension(sourcePath), ".galpak", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A .galpak package is required.", nameof(packagePath));
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Game package was not found.", sourcePath);

        using var source = File.OpenRead(sourcePath);
        using var zip = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: false);
        const string prefix = "pak/";
        var manifestEntry = zip.GetEntry(prefix + PackedGameDocuments.GameFileName)
            ?? throw new InvalidDataException("A .galpak must contain pak/game.pak with a complete v4 installation index.");
        byte[] gameBytes;
        await using (var input = manifestEntry.Open())
        using (var buffer = new MemoryStream())
        {
            await input.CopyToAsync(buffer, ct);
            gameBytes = buffer.ToArray();
        }
        var packed = PackedGameDocuments.ReadGame(gameBytes);
        var manifest = packed.Installation;
        ValidateManifest(manifest, zip, manifestEntry.FullName, prefix);
        var targetDirectory = installationDirectory is null
            ? Path.Combine(Path.GetDirectoryName(sourcePath)!, "pak")
            : Path.GetFullPath(installationDirectory);

        if (Directory.Exists(targetDirectory))
        {
            if (await MatchesInstalledPackageAsync(targetDirectory, manifest, ct)
                && File.Exists(Path.Combine(targetDirectory, PackedGameDocuments.GameFileName))
                && (await File.ReadAllBytesAsync(Path.Combine(targetDirectory, PackedGameDocuments.GameFileName), ct)).AsSpan().SequenceEqual(gameBytes))
            {
                foreach (var payload in manifest.Files)
                {
                    await using var input = zip.GetEntry(prefix + payload.Path)!.Open();
                    if (Convert.ToHexStringLower(await SHA256.HashDataAsync(input, ct)) != payload.Sha256)
                        throw new InvalidDataException($"Package entry '{payload.Path}' checksum does not match its manifest.");
                }
                InstalledGameDescriptor.EnsureExtensionDirectories(targetDirectory, packed.Manifest.BuildId);
                return targetDirectory;
            }
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
                var sourceEntry = zip.GetEntry(prefix + package.Path) ?? throw new InvalidDataException($"Package is missing '{prefix}{package.Path}'.");
                var destinationPath = GetSafeDestination(stagingDirectory, package.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                await CopyAndVerifyAsync(sourceEntry, destinationPath, package, ct);
            }

            var installedManifest = GetSafeDestination(stagingDirectory, manifestEntry.FullName[prefix.Length..]);
            Directory.CreateDirectory(Path.GetDirectoryName(installedManifest)!);
            await using (var manifestSource = manifestEntry.Open())
            await using (var manifestDestination = File.Create(installedManifest))
                await manifestSource.CopyToAsync(manifestDestination, ct);

            InstalledGameDescriptor.EnsureExtensionDirectories(stagingDirectory, packed.Manifest.BuildId);
            Directory.Move(stagingDirectory, targetDirectory);
            return targetDirectory;
        }
        catch
        {
            if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
            throw;
        }
    }

    private static void ValidateManifest(GalpakManifest manifest, ZipArchive zip, string manifestPath, string prefix)
    {
        if (manifest.Files is not { Count: > 0 }) throw new InvalidDataException("Package manifest has no payloads.");

        var archivePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            if (!archivePaths.Add(entry.FullName)) throw new InvalidDataException($"Duplicate ZIP entry '{entry.FullName}'.");
            if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidDataException("All package entries must be inside the declared content root.");
            if (entry.FullName.EndsWith('/'))
            {
                if (entry.FullName != prefix) ValidateRelativePath(entry.FullName[prefix.Length..].TrimEnd('/'));
            }
            else ValidateRelativePath(entry.FullName[prefix.Length..]);
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in manifest.Files)
        {
            if (package.Size < 0 || string.IsNullOrWhiteSpace(package.Sha256) || package.Sha256.Length != 64 || !package.Sha256.All(char.IsAsciiHexDigit))
                throw new InvalidDataException($"Package manifest entry '{package.Path}' has an invalid checksum or size.");
            ValidateRelativePath(package.Path);
            if (prefix + package.Path == manifestPath) throw new InvalidDataException("The package index cannot include itself.");
            if (package.Path.StartsWith("I18n/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("I18n is reserved for player-installed translations, not package payloads.");
            if (package.Path.EndsWith(".galnet", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The package manifest cannot also be a payload file.");
            if (!paths.Add(package.Path)) throw new InvalidDataException($"Package manifest repeats '{package.Path}'.");
            var zipEntry = zip.GetEntry(prefix + package.Path) ?? throw new InvalidDataException($"Package is missing '{prefix}{package.Path}'.");
            if (zipEntry.Length != package.Size) throw new InvalidDataException($"Package entry '{package.Path}' size does not match its manifest.");
        }
        if (prefix.Length > 0 && zip.Entries.Any(entry => !entry.FullName.EndsWith('/')
            && entry.FullName != manifestPath && !paths.Contains(entry.FullName[prefix.Length..])))
            throw new InvalidDataException("Package contains an undeclared payload.");
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

    private static string GetSafeDestination(string root, string relativePath) => FileSystemPaths.ResolveRelativeFile(root, relativePath);

    private static void ValidateRelativePath(string path) => FileSystemPaths.ValidateRelativeFile(path);
}
