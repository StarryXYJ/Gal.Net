using GalNet.Core.Gallery;
using GalNet.Core.Handler;
using GalNet.Core.Services;

namespace GalNet.Runtime.Handlers;

/// <summary>Persists a gallery unlock outside the active save slot.</summary>
public sealed class UnlockGalleryHandler : EntryHandler
{
    private readonly IGameProgressService _progress;
    public UnlockGalleryHandler(IGameProgressService progress) => _progress = progress;
    public override string EntryType => "unlock_gallery";
    public override bool IsBlocking => false;
    public override void Start(EntryContext ctx)
    {
        if (!Enum.TryParse<GalleryCategory>(ctx.GetString("category"), true, out var category)) return;
        if (!int.TryParse(ctx.GetString("id"), out var id) || id < 0) return;
        _progress.UnlockGallery(category, id);
    }
}
