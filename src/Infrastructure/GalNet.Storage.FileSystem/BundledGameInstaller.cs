using System.Security.Cryptography;

namespace GalNet.Storage.FileSystem;

/// <summary>Materializes a platform asset stream into a validated, versioned writable content directory.</summary>
public static class BundledGameInstaller
{
    public static async Task<string> InstallAsync(Stream package, string contentCacheDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!Path.IsPathFullyQualified(contentCacheDirectory)) throw new ArgumentException("The content cache must be absolute.");
        Directory.CreateDirectory(contentCacheDirectory);
        var temporary = Path.Combine(contentCacheDirectory, ".incoming-" + Guid.NewGuid().ToString("N") + ".galpak");
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await package.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            var target = Path.Combine(contentCacheDirectory, Convert.ToHexStringLower(hash.GetHashAndReset()));
            return await GalpakInstaller.InstallAsync(temporary, target, cancellationToken);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
