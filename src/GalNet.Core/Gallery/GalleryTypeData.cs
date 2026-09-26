namespace GalNet.Core.Gallery;

/// <summary>One registered Gallery type and the items currently exposed for it.</summary>
public sealed record GalleryTypeData(
    GalleryTypeRegistration Type,
    IReadOnlyList<GalleryItemData> Items);
