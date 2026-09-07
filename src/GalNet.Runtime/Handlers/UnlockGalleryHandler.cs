using GalNet.Core.Gallery;
using GalNet.Core.Services;

namespace GalNet.Runtime.Handlers;

/// <summary>Persists a gallery unlock outside the active save slot.</summary>
public sealed class UnlockGalleryHandler : EntryHandler
{
    private readonly IGameProgressService _progress;
    public UnlockGalleryHandler(IGameProgressService progress) => _progress = progress;
    public override string EntryType => "unlock_gallery";
    public override Task ExecuteAsync(EntryContext context, GalNet.Core.View.IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        if (!Enum.TryParse<GalleryCategory>(context.GetString("category"), true, out var category)) return Task.CompletedTask;
        if (!int.TryParse(context.GetString("id"), out var id) || id < 0) return Task.CompletedTask;
        _progress.UnlockGallery(category, id);
        return Task.CompletedTask;
    }
}
