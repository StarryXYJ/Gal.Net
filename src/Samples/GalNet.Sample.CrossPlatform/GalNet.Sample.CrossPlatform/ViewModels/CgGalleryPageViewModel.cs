using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Contracts.Builtins.Gallery;
namespace GalNet.Sample.CrossPlatform.ViewModels;

public sealed class CgGalleryPageViewModel(IGameNavigationService navigation, GalleryMediaService media)
    : GalleryGridPageViewModel(navigation, media)
{
    protected override Task<global::Avalonia.Media.Imaging.Bitmap?> LoadThumbnailAsync(GalleryItemData item, CancellationToken cancellationToken) => Media.LoadImageThumbnailAsync(item, cancellationToken);
    protected override Task OpenDetailAsync(GalleryItemData item, CancellationToken cancellationToken) => Navigation.NavigateAsync<CgGalleryImagePageViewModel, GalleryItemData>(item, cancellationToken);
}
