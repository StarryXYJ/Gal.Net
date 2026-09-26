namespace GalNet.Core.Gallery;

/// <summary>Creates the default Gallery type registrations offered by GalNet authoring hosts.</summary>
public static class BuiltinGalleryTypes
{
    public static GalleryConfiguration CreateConfiguration() => new()
    {
        Types =
        [
            new GalleryTypeRegistration { TypeId = "cg", ResourceTypeName = "sprite" },
            new GalleryTypeRegistration { TypeId = "video", ResourceTypeName = "video" },
            new GalleryTypeRegistration { TypeId = "audio", ResourceTypeName = "audio" }
        ]
    };
}
