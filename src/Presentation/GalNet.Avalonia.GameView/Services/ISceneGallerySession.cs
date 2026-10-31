using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Contracts.Builtins.Gallery;

namespace GalNet.Avalonia.GameView.Services;

/// <summary>Optional host capability. A replay owns a separate surface and never saves player progress.</summary>
public interface ISceneGallerySession
{
    Task<IAsyncDisposable> StartSceneAsync(GalleryItemData item, GamePageViewModel model, GamePage surface,
        Action completed, CancellationToken cancellationToken);
}
