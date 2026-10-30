using GalNet.Storage.FileSystem;

namespace GalNet.Sample.Player;

public static class SamplePlayerPaths
{
    public static string GetProfileDirectory(string gameDirectory, string? profileRoot = null) =>
        GameProfilePaths.GetProfileDirectory(gameDirectory,
            profileRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GalNet"));
}
