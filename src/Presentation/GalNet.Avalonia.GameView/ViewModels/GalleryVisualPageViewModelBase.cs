using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Contracts.Builtins.Gallery;
namespace GalNet.Avalonia.GameView.ViewModels;
// Transitional page adapter for legacy layouts; removed with their registration in P5.
public abstract partial class GalleryVisualPageViewModelBase : BackNavigablePageViewModel, IDisposable
{
    public GalleryThumbnailCollection Thumbnails { get; }
    protected GalleryVisualPageViewModelBase(IGameNavigationService navigation, GalleryMediaService media) : base(navigation)
    {
        Media = media;
        Thumbnails = new(media);
        Thumbnails.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
    }
    public ObservableCollection<GalleryEntryViewModel> Items => Thumbnails.Items;
    public string Title => Thumbnails.Title;
    protected GalleryMediaService Media { get; }
    protected Task ActivateGalleryAsync(GalleryTypeData gallery, Func<GalleryItemData, CancellationToken, Task<global::Avalonia.Media.Imaging.Bitmap?>> load, CancellationToken token) => Thumbnails.ActivateAsync(gallery, load, token);
    protected abstract Task OpenDetailAsync(GalleryItemData item, CancellationToken token);
    [RelayCommand] private Task OpenItemAsync(GalleryEntryViewModel item, CancellationToken token) => item.IsUnlocked ? OpenDetailAsync(item.Data, token) : Task.CompletedTask;
    public void Dispose() => Thumbnails.Dispose();
}
