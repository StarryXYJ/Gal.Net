using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Contracts.Builtins.Gallery;
namespace GalNet.Sample.CrossPlatform.ViewModels;

public abstract partial class GalleryGridPageViewModel(IGameNavigationService navigation, GalleryMediaService media)
    : BackNavigablePageViewModel(navigation), IGalleryPageViewModel, IDisposable
{
    protected GalleryMediaService Media => media;
    public GalleryThumbnailCollection Thumbnails { get; } = new(media);
    public abstract Task ActivateAsync(GalleryTypeData gallery, CancellationToken cancellationToken = default);
    protected abstract Task OpenDetailAsync(GalleryItemData item, CancellationToken cancellationToken);
    [RelayCommand] private Task OpenItemAsync(GalleryEntryViewModel item, CancellationToken cancellationToken) => item.IsUnlocked ? OpenDetailAsync(item.Data, cancellationToken) : Task.CompletedTask;
    public void Dispose() => Thumbnails.Dispose();
}
