using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Contracts.Builtins.Gallery;
namespace GalNet.Sample.CrossPlatform.ViewModels;

public sealed class VideoGalleryPlayerPageViewModel : BackNavigablePageViewModel, IActivatablePageViewModel<GalleryItemData>, IDisposable
{
    public GalleryVideoState State { get; }
    public VideoGalleryPlayerPageViewModel(IGameNavigationService navigation, GalleryMediaService media) : base(navigation)
    {
        State = new(media);
    }
    public Task ActivateAsync(GalleryItemData item, CancellationToken cancellationToken = default) => State.ActivateAsync(item, cancellationToken);
    protected override void OnBack() { State.Stop(); base.OnBack(); }
    public void Dispose() => State.Dispose();
}
