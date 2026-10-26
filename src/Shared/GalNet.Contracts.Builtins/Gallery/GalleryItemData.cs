namespace GalNet.Contracts.Builtins.Gallery;

/// <summary>One Gallery item projected with its current Player unlock state.</summary>
public sealed record GalleryItemData(GalleryItem Item, bool IsUnlocked);
