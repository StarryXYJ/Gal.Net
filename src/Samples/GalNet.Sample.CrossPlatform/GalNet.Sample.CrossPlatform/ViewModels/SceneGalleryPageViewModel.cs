using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Contracts.Builtins.Gallery;
namespace GalNet.Sample.CrossPlatform.ViewModels;

public sealed class SceneGalleryPageViewModel(IGameNavigationService navigation, GalleryMediaService media, SceneGalleryPlayerPageViewModel player)
    : GalleryGridPageViewModel(navigation, media)
{
    protected override Task<global::Avalonia.Media.Imaging.Bitmap?> LoadThumbnailAsync(GalleryItemData item, CancellationToken cancellationToken) => Media.LoadSceneThumbnailAsync(item, cancellationToken);
    protected override async Task OpenDetailAsync(GalleryItemData item, CancellationToken cancellationToken)
    {
        await Navigation.NavigateAsync(player, cancellationToken: cancellationToken);
        await player.ActivateAsync(item, cancellationToken);
    }
}
