using GalNet.Assets;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Assets;

namespace GalNet.Avalonia.GameView.Services;

/// <summary>Retains shared materialized file leases for Gallery media.</summary>
public sealed class AssetGalleryResourceResolver : IGalleryResourceResolver, IDisposable
{
    private readonly IAssetFileMaterializer _files;
    private readonly bool _ownsFiles;
    private readonly Dictionary<string, AssetFileLease> _leases = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    private AssetGalleryResourceResolver(IAssetManager assets, IAssetFileMaterializer? files)
    {
        _files = files ?? new AssetFileMaterializer(assets);
        _ownsFiles = files is null;
    }

    public static async Task<AssetGalleryResourceResolver> CreateAsync(IAssetManager assets, GalleryCatalog gallery,
        CancellationToken ct = default, IAssetFileMaterializer? files = null)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(gallery);
        var resolver = new AssetGalleryResourceResolver(assets, files);
        try
        {
            foreach (var id in gallery.Items.Select(item => item.ResourceId).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try { resolver._leases.Add(id, await resolver._files.AcquireAsync(id, ct).ConfigureAwait(false)); }
                catch (FileNotFoundException) { } // Missing optional Gallery media remains unavailable.
            }
            return resolver;
        }
        catch { resolver.Dispose(); throw; }
    }

    public string? ResolvePath(string resourceId) => _leases.GetValueOrDefault(resourceId)?.Path;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { foreach (var lease in _leases.Values) lease.Dispose(); }
        finally
        {
            _leases.Clear();
            if (_ownsFiles) _files.Dispose();
        }
    }
}
