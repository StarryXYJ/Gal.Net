namespace GalNet.Avalonia.GameView.Services;

/// <summary>Host adapter that maps a Gallery resource ID to a local media path.</summary>
public interface IGalleryResourceResolver
{
    string? ResolvePath(string resourceId);
}
