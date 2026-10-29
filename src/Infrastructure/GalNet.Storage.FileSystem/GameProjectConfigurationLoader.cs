using System.Text.Json;
using GalNet.Core.Configuration;
using GalNet.Core.Serialization;

namespace GalNet.Storage.FileSystem;

internal static class GameProjectConfigurationLoader
{
    public static ProjectConfiguration Load(string directory, ProjectConfigurationSchema? schema = null)
    {
        var path = Path.Combine(directory, "settings.json");
        if (!File.Exists(path)) return new ProjectConfiguration();
        return LoadJson(File.ReadAllText(path), schema, path);
    }

    internal static ProjectConfiguration LoadJson(string json, ProjectConfigurationSchema? schema, string sourceName)
    {
        try
        {
            var settings = JsonCodec.Deserialize<ProjectConfiguration>(json)
                ?? throw new InvalidDataException("Project settings must be a JSON object.");
            schema?.Validate(settings);
            return settings;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"Invalid project settings in '{sourceName}'.", error);
        }
    }
}
