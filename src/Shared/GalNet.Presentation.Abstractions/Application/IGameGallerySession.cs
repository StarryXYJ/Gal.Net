using GalNet.Contracts.Builtins.Gallery;

namespace GalNet.Presentation.Abstractions.Application;

/// <summary>Optional session capability consumed by the default Gallery front end.</summary>
public interface IGameGallerySession
{
    IGalleryDataSource? GalleryDataSource { get; }
    IGalleryResourceResolver? GalleryResources { get; }
}
