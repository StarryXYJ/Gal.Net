using System.Collections.ObjectModel;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Core.Gallery;

namespace GalNet.Avalonia.GameView.ViewModels;

/// <summary>Shared lifecycle for Gallery pages that display a thumbnail grid.</summary>
public abstract partial class GalleryVisualPageViewModelBase(
    IGameNavigationService navigation,
    GalleryMediaService media) : PageViewModelBase, IDisposable
{
    private CancellationTokenSource? _thumbnailCancellation;
    private GalleryTypeData? _gallery;

    public ObservableCollection<GalleryEntryViewModel> Items { get; } = [];
    public string Title => _gallery?.Type.TypeId ?? "Gallery";

    protected async Task ActivateGalleryAsync(
        GalleryTypeData gallery,
        Func<GalleryItemData, CancellationToken, Task<global::Avalonia.Media.Imaging.Bitmap?>> loadThumbnail,
        CancellationToken cancellationToken)
    {
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        foreach (var item in Items) item.Dispose();
        Items.Clear();
        _gallery = gallery;
        OnPropertyChanged(nameof(Title));

        foreach (var item in gallery.Items.OrderBy(item => item.Item.Id))
            Items.Add(new GalleryEntryViewModel(item));

        try
        {
            foreach (var item in Items.Where(item => item.IsUnlocked))
            {
                var thumbnail = await loadThumbnail(item.Data, _thumbnailCancellation.Token);
                if (_thumbnailCancellation.IsCancellationRequested)
                {
                    thumbnail?.Dispose();
                    return;
                }
                item.Thumbnail = thumbnail;
            }
        }
        catch (OperationCanceledException) when (_thumbnailCancellation.IsCancellationRequested) { }
    }

    protected abstract Task OpenDetailAsync(GalleryItemData item, CancellationToken cancellationToken);

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task OpenItemAsync(GalleryEntryViewModel item, CancellationToken cancellationToken) =>
        item.IsUnlocked ? OpenDetailAsync(item.Data, cancellationToken) : Task.CompletedTask;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void Back() => navigation.GoBack();

    public void Dispose()
    {
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = null;
        foreach (var item in Items) item.Dispose();
        Items.Clear();
    }

    protected IGameNavigationService Navigation => navigation;
    protected GalleryMediaService Media => media;
}
