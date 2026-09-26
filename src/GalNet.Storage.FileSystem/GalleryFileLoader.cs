using System.Text.Json;
using GalNet.Core.Gallery;

namespace GalNet.Storage.FileSystem;

/// <summary>Loads and validates an optional <c>gallery.json</c> file.</summary>
public static class GalleryFileLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static GalleryCatalog LoadOptional(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            return GalleryCatalog.Empty;

        try
        {
            var configuration = JsonSerializer.Deserialize<GalleryConfiguration>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException($"The Gallery file '{path}' is empty.");
            return GalleryCatalog.Create(configuration);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The Gallery file '{path}' is not valid JSON.", exception);
        }
    }
}
