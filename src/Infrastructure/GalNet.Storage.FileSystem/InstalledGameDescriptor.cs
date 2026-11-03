using GalNet.Core.Serialization;
using GalNet.Assets;

namespace GalNet.Storage.FileSystem;

/// <summary>Reads a content identity from its fixed packed entry.</summary>
public static class InstalledGameDescriptor
{
    public static GameContentManifest Read(string directory)
    {
        var packed = Path.Combine(directory, PackedGameDocuments.GameFileName);
        if (!File.Exists(packed)) throw new InvalidDataException("Installed content requires game.pak. Re-export the game.");
        return PackedGameDocuments.ReadGame(File.ReadAllBytes(packed)).Manifest;
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
