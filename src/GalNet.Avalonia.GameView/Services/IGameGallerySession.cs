using GalNet.Core.Gallery;

namespace GalNet.Avalonia.GameView.Services;

/// <summary>Optional session capability consumed by the default Gallery front end.</summary>
public interface IGameGallerySession
{
    IGalleryDataSource? GalleryDataSource { get; }
    IGalleryResourceResolver? GalleryResources { get; }
}
