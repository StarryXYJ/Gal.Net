namespace GalNet.Editor.Models;

public enum AssetPickerFilter
{
    All,
    Image,
    Audio,
    Video,
    Text
}

public static class AssetPickerTextFileExtensions
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".json", ".csv", ".xml", ".html", ".htm"
    };

    public static bool IsSupported(string path) => Supported.Contains(Path.GetExtension(path));
}
