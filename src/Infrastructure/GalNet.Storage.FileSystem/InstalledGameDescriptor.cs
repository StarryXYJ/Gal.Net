using System.Text.Json;
using GalNet.Assets;
using GalNet.Core.Serialization;

namespace GalNet.Storage.FileSystem;

/// <summary>Reads a content identity from its fixed packed entry, with legacy directory compatibility.</summary>
public static class InstalledGameDescriptor
{
    public static GameContentManifest Read(string directory)
    {
        var packed = Path.Combine(directory, PackedGameDocuments.GameFileName);
        if (File.Exists(packed)) return PackedGameDocuments.ReadGame(File.ReadAllBytes(packed)).Manifest;
        return JsonCodec.Deserialize<GameContentManifest>(File.ReadAllText(Path.Combine(directory, GameContentManifest.FileName)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { AllowDuplicateProperties = false })
            ?? throw new InvalidDataException("Missing content descriptor.");
    }

    internal static void EnsureExtensionDirectories(string directory, string buildId)
    {
        foreach (var relative in PackedGameDocuments.ExtensionDirectories(buildId))
        {
            var path = FileSystemPaths.ResolveRelativeFile(directory, relative);
            FileSystemPaths.RequireNoLinks(path, "Extension directories cannot be symbolic links.");
            Directory.CreateDirectory(path);
        }
    }
}
