using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Core.Gallery;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed class CgGalleryPageViewModel(IGameNavigationService navigation, GalleryMediaService media)
    : GalleryVisualPageViewModelBase(navigation, media), IGalleryPageViewModel
{
    public Task ActivateAsync(GalleryTypeData args, CancellationToken cancellationToken = default) =>
        ActivateGalleryAsync(args, Media.LoadImageThumbnailAsync, cancellationToken);

    protected override Task OpenDetailAsync(GalleryItemData item, CancellationToken cancellationToken) =>
        Navigation.NavigateAsync<CgGalleryImagePageViewModel, GalleryItemData>(item, cancellationToken);
}
