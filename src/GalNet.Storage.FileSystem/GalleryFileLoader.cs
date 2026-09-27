using System.Text.Json;
using GalNet.Core.Gallery;

namespace GalNet.Storage.FileSystem;

/// <summary>Loads generated Gallery content and verifies its type snapshot against the composed registry.</summary>
public static class GalleryFileLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static GalleryCatalog LoadGenerated(string path, IGalleryTypeCatalog expectedTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(expectedTypes);
        if (!File.Exists(path)) throw new FileNotFoundException("Generated Gallery content was not found.", path);

        return LoadGeneratedJson(File.ReadAllText(path), expectedTypes, path);
    }

    /// <summary>Loads generated Gallery JSON supplied by a package instead of a directory file.</summary>
    public static GalleryCatalog LoadGeneratedJson(string json, IGalleryTypeCatalog expectedTypes, string sourceName = "gallery.json")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(expectedTypes);
        try
        {
            var configuration = JsonSerializer.Deserialize<GalleryConfiguration>(json, JsonOptions)
                ?? throw new InvalidDataException($"The Gallery file '{sourceName}' is empty.");
            var catalog = GalleryCatalog.Create(configuration);
            if (catalog.Types.Count != expectedTypes.Types.Count || catalog.Types.Any(type =>
                    !expectedTypes.TryGet(type.TypeId, out var expected) ||
                    !string.Equals(expected.ResourceTypeId, type.ResourceTypeId, StringComparison.Ordinal)))
                throw new InvalidDataException("Generated Gallery type snapshot does not match the composed Gallery registry.");
            return catalog;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The Gallery file '{sourceName}' is not valid JSON.", exception);
        }
    }
}
