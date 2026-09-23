using GalNet.Core.Gallery;

namespace GalNet.Core.Services;

/// <summary>Per-player progress that must not be restored by a save slot.</summary>
public interface IGameProgressService
{
    bool IsRead(string groupId, string entryId);
    void MarkRead(string groupId, string entryId);
    bool IsGalleryUnlocked(GalleryCategory category, int sequenceId);
    void UnlockGallery(GalleryCategory category, int sequenceId);
}
