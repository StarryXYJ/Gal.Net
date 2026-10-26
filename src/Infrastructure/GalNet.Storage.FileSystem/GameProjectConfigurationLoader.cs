using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Configuration;

namespace GalNet.Storage.FileSystem;

internal static class GameProjectConfigurationLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static ProjectConfiguration Load(string directory, ProjectConfigurationSchema? schema = null)
    {
        var path = Path.Combine(directory, "settings.json");
        if (!File.Exists(path)) return new ProjectConfiguration();
        try
        {
            var settings = JsonSerializer.Deserialize<ProjectConfiguration>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException("Project settings must be a JSON object.");
            schema?.Validate(settings);
            return settings;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"Invalid project settings in '{path}'.", error);
        }
    }
}
