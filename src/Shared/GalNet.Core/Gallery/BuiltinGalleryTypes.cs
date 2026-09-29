namespace GalNet.Core.Gallery;

/// <summary>Creates the default Gallery type registrations offered by GalNet authoring hosts.</summary>
public static class BuiltinGalleryTypes
{
    public static IGalleryTypeCatalog CreateCatalog(GalNet.Core.Assets.IResourceTypeCatalog resourceTypes) => new GalleryTypeCatalogBuilder()
        .Add("cg", "sprite")
        .Add("video", "video")
        .Add("audio", "audio")
        .Build(resourceTypes);
}
