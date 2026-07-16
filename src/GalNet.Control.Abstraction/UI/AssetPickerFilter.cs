namespace GalNet.Control.Abstraction.UI;

/// <summary>Media categories exposed by UI asset-picking controls.</summary>
public enum AssetPickerFilter { All, Image, Audio, Video, Text }

public static class AssetPickerTextFileExtensions
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".ini", ".json", ".jsonc", ".yaml", ".yml",
        ".toml", ".xml", ".csv", ".tsv", ".log", ".cfg", ".conf"
    };

    public static bool IsSupported(string path) => Supported.Contains(Path.GetExtension(path));
}
