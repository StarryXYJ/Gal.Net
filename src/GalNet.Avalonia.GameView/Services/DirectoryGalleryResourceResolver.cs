using System.Text.Json;
using GalNet.Core.Assets;

namespace GalNet.Avalonia.GameView.Services;

/// <summary>Resolves asset IDs from the sidecar metadata in a local Assets directory.</summary>
public sealed class DirectoryGalleryResourceResolver : IGalleryResourceResolver
{
    private readonly Dictionary<string, string> _paths = new(StringComparer.Ordinal);

    public DirectoryGalleryResourceResolver(string? assetsDirectory)
    {
        if (string.IsNullOrWhiteSpace(assetsDirectory) || !Directory.Exists(assetsDirectory))
            return;

        foreach (var metaPath in Directory.EnumerateFiles(assetsDirectory, "*.meta", SearchOption.AllDirectories))
        {
            try
            {
                var meta = JsonSerializer.Deserialize<AssetMeta>(File.ReadAllText(metaPath));
                if (meta is null || string.IsNullOrWhiteSpace(meta.Id)) continue;
                var sourcePath = metaPath[..^".meta".Length];
                if (File.Exists(sourcePath)) _paths.TryAdd(meta.Id, sourcePath);
            }
            catch (JsonException)
            {
                // Invalid asset metadata is diagnosed by the authoring/export pipeline.
            }
        }
    }

    public string? ResolvePath(string resourceId) => _paths.GetValueOrDefault(resourceId);
}
