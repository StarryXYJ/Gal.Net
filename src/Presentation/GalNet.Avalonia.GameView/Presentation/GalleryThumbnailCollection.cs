using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Avalonia.GameView.Services;
using GalNet.Contracts.Builtins.Gallery;

namespace GalNet.Avalonia.GameView.Presentation;

/// <summary>Shared lifecycle for Gallery pages that display a thumbnail grid.</summary>
public sealed class GalleryThumbnailCollection(GalleryMediaService media) : ObservableObject, IDisposable
{
    private CancellationTokenSource? _thumbnailCancellation;
    private GalleryTypeData? _gallery;

    public ObservableCollection<GalleryEntryViewModel> Items { get; } = [];
    public string Title => _gallery is { } gallery ? media.ResolveText(gallery.Type.DisplayNameKey, gallery.Type.TypeId) : "Gallery";

    public async Task ActivateAsync(
        GalleryTypeData gallery,
        Func<GalleryItemData, CancellationToken, Task<global::Avalonia.Media.Imaging.Bitmap?>> loadThumbnail,
        CancellationToken cancellationToken)
    {
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var lifetime = _thumbnailCancellation;
        foreach (var item in Items) item.Dispose();
        Items.Clear();
        _gallery = gallery;
        OnPropertyChanged(nameof(Title));

        foreach (var item in gallery.Items.OrderBy(item => item.Item.Id))
            Items.Add(new GalleryEntryViewModel(item, media.ResolveName(item.Item), unnamedTitle: media.ResolveText("Gallery.Untitled", "Untitled")));

        try
        {
            foreach (var item in Items.Where(item => item.IsUnlocked).ToArray())
            {
                var thumbnail = await loadThumbnail(item.Data, lifetime.Token);
                if (lifetime.IsCancellationRequested)
                {
                    thumbnail?.Dispose();
                    return;
                }
                item.Thumbnail = thumbnail;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = null;
        foreach (var item in Items) item.Dispose();
        Items.Clear();
        GC.SuppressFinalize(this);
    }


}
