namespace GalNet.Core.Gallery;

/// <summary>Platform-independent Gallery projection consumed by host UI implementations.</summary>
public interface IGalleryDataSource
{
    IReadOnlyList<GalleryTypeData> GetTypes();
}
