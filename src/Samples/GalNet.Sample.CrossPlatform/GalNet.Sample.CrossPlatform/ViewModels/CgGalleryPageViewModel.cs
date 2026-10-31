using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Contracts.Builtins.Gallery;
namespace GalNet.Sample.CrossPlatform.ViewModels;

public sealed class CgGalleryPageViewModel(IGameNavigationService navigation, GalleryMediaService media)
    : GalleryGridPageViewModel(navigation, media)
{
    public override Task ActivateAsync(GalleryTypeData gallery, CancellationToken cancellationToken = default) => Thumbnails.ActivateAsync(gallery, Media.LoadImageThumbnailAsync, cancellationToken);
    protected override Task OpenDetailAsync(GalleryItemData item, CancellationToken cancellationToken) => Navigation.NavigateAsync<CgGalleryImagePageViewModel, GalleryItemData>(item, cancellationToken);
}
