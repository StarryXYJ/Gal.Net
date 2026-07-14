using GalNet.Core.Gallery;

namespace GalNet.Core.Services;

/// <summary>Per-player, per-game progress which must not be restored by save slots.</summary>
public interface IGameProgressService
{
    bool IsRead(string groupId, string entryId);
    void MarkRead(string groupId, string entryId);
    bool IsGalleryUnlocked(GalleryCategory category, int sequenceId);
    void UnlockGallery(GalleryCategory category, int sequenceId);
}
