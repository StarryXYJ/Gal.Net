using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Contracts.Builtins.Gallery;
namespace GalNet.Sample.CrossPlatform.ViewModels;

public sealed class CgGalleryImagePageViewModel : BackNavigablePageViewModel, IActivatablePageViewModel<GalleryItemData>, IDisposable
{
    public GalleryImageState State { get; }
    public CgGalleryImagePageViewModel(IGameNavigationService navigation, GalleryMediaService media) : base(navigation)
    {
        State = new(media);
    }
    public Task ActivateAsync(GalleryItemData item, CancellationToken cancellationToken = default) => State.ActivateAsync(item, cancellationToken);
    public void Dispose() => State.Dispose();
}
