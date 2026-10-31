using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Contracts.Builtins.Gallery;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed class SceneGalleryPageViewModel(IGameNavigationService navigation, GalleryMediaService media, SceneGalleryPlayerPageViewModel player)
    : GalleryVisualPageViewModelBase(navigation, media), IGalleryPageViewModel
{
    public Task ActivateAsync(GalleryTypeData args, CancellationToken cancellationToken = default) =>
        ActivateGalleryAsync(args, Media.LoadSceneThumbnailAsync, cancellationToken);
    protected override async Task OpenDetailAsync(GalleryItemData item, CancellationToken cancellationToken)
    {
        await Navigation.NavigateAsync(player, cancellationToken: cancellationToken);
        await player.ActivateAsync(item, cancellationToken);
    }
}
