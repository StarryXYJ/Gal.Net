namespace GalNet.Avalonia.GameView.ViewModels;

/// <summary>Maps registered Gallery metadata to one of the built-in Avalonia renderers.</summary>
public static class GalleryRendererResolver
{
    public static GalleryRendererKind Resolve(string typeId, string resourceTypeName)
    {
        if (string.Equals(typeId, "cg", StringComparison.OrdinalIgnoreCase)) return GalleryRendererKind.Image;
        if (string.Equals(typeId, "video", StringComparison.OrdinalIgnoreCase)) return GalleryRendererKind.Video;
        if (string.Equals(typeId, "audio", StringComparison.OrdinalIgnoreCase)) return GalleryRendererKind.Audio;

        if (string.Equals(resourceTypeName, "sprite", StringComparison.OrdinalIgnoreCase)
            || string.Equals(resourceTypeName, "image", StringComparison.OrdinalIgnoreCase))
            return GalleryRendererKind.Image;
        if (string.Equals(resourceTypeName, "video", StringComparison.OrdinalIgnoreCase))
            return GalleryRendererKind.Video;
        if (string.Equals(resourceTypeName, "audio", StringComparison.OrdinalIgnoreCase))
            return GalleryRendererKind.Audio;
        return GalleryRendererKind.Unsupported;
    }
}
