using System.Security.Cryptography;
using System.Text;
using GalNet.Core.Assets;
using GalNet.Core.Gallery;

namespace GalNet.Avalonia.GameView.Services;

/// <summary>Materializes Gallery media acquired from the asset manager when a platform path is required.</summary>
public sealed class AssetGalleryResourceResolver : IGalleryResourceResolver, IDisposable
{
    private readonly string _directory;
    private readonly Dictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    private AssetGalleryResourceResolver(string directory) => _directory = directory;

    public static async Task<AssetGalleryResourceResolver> CreateAsync(IAssetManager assets, GalleryCatalog gallery, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(gallery);
        var resolver = new AssetGalleryResourceResolver(Path.Combine(Path.GetTempPath(), "GalNet", "GalleryMedia", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(resolver._directory);
        try
        {
            foreach (var resourceId in gallery.Items.Select(item => item.ResourceId).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                var file = await assets.GetFileAsync(resourceId, ct);
                if (file is null) continue;
                using var handle = await assets.AcquireAsync<byte[]>(resourceId, ct);
                if (handle is null) continue;

                var extension = Path.GetExtension(file.Path);
                var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resourceId))).ToLowerInvariant();
                var path = Path.Combine(resolver._directory, name + extension);
                await File.WriteAllBytesAsync(path, handle.Value, ct);
                resolver._paths.Add(resourceId, path);
            }
            return resolver;
        }
        catch
        {
            resolver.Dispose();
            throw;
        }
    }

    public string? ResolvePath(string resourceId) => _paths.GetValueOrDefault(resourceId);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _paths.Clear();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
