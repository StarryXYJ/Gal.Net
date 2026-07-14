namespace GalNet.Core.Gallery;

public enum GalleryCategory { Portrait, Cg, Scene }

public sealed class GalleryConfiguration
{
    public List<GalleryItem> Items { get; set; } = [];
}

/// <summary>SequenceId is zero-based and stable within its category.</summary>
public sealed class GalleryItem
{
    public GalleryCategory Category { get; set; }
    public int SequenceId { get; set; }
    public string ResourceId { get; set; } = "";
    public bool IsVideo { get; set; }
    public string? Title { get; set; }
}
