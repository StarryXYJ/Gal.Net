using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Contracts.Builtins.Gallery;
namespace GalNet.Sample.CrossPlatform.ViewModels;

public sealed class SceneGalleryPageViewModel(IGameNavigationService navigation, GalleryMediaService media, SceneGalleryPlayerPageViewModel player)
    : GalleryGridPageViewModel(navigation, media)
{
    public override Task ActivateAsync(GalleryTypeData gallery, CancellationToken cancellationToken = default) => Thumbnails.ActivateAsync(gallery, Media.LoadSceneThumbnailAsync, cancellationToken);
    protected override async Task OpenDetailAsync(GalleryItemData item, CancellationToken cancellationToken)
    {
        await Navigation.NavigateAsync(player, cancellationToken: cancellationToken);
        await player.ActivateAsync(item, cancellationToken);
    }
}
