using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Page;
using GalNet.Contracts.Builtins.Gallery;

namespace GalNet.Avalonia.GameView.Services;

/// <summary>Optional host capability. A replay owns a separate surface and never saves player progress.</summary>
public interface ISceneGallerySession
{
    Task<IAsyncDisposable> StartSceneAsync(GalleryItemData item, GamePresentationState model, GamePage surface,
        Action completed, CancellationToken cancellationToken);
}
