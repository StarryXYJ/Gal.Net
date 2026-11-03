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
    private IReadOnlyList<GalleryEntryViewModel> _loadingRows = [];
    private GalleryTypeData? _gallery;

    public ObservableCollection<GalleryEntryViewModel> Items { get; } = [];
    public string Title => _gallery is { } gallery ? media.ResolveText(gallery.Type.DisplayNameKey, gallery.Type.TypeId) : "Gallery";

    public async Task ActivateAsync(
        GalleryTypeData gallery,
        Func<GalleryItemData, CancellationToken, Task<global::Avalonia.Media.Imaging.Bitmap?>> loadThumbnail,
        CancellationToken cancellationToken)
    {
        Prepare(gallery);
        await LoadAsync(Items.ToArray(), loadThumbnail, cancellationToken);
    }

    public void Prepare(GalleryTypeData gallery)
    {
        CancelLoading();
        foreach (var item in Items) item.Dispose();
        Items.Clear();
        _gallery = gallery;
        OnPropertyChanged(nameof(Title));

        foreach (var item in gallery.Items.OrderBy(item => item.Item.Id))
            Items.Add(new GalleryEntryViewModel(item, media.ResolveName(item.Item), unnamedTitle: media.ResolveText("Gallery.Untitled", "Untitled")));
    }

    public async Task LoadAsync(IReadOnlyList<GalleryEntryViewModel> rows,
        Func<GalleryItemData, CancellationToken, Task<global::Avalonia.Media.Imaging.Bitmap?>> loadThumbnail,
        CancellationToken cancellationToken)
    {
        CancelLoading();
        _thumbnailCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var lifetime = _thumbnailCancellation;
        var token = lifetime.Token;
        var pending = rows.Where(item => item.IsUnlocked).ToArray();
        _loadingRows = pending;
        foreach (var item in pending) item.IsThumbnailLoading = true;
        try
        {
            foreach (var item in pending)
            {
                token.ThrowIfCancellationRequested();
                var thumbnail = await loadThumbnail(item.Data, token);
                if (lifetime.IsCancellationRequested)
                {
                    thumbnail?.Dispose();
                    return;
                }
                item.Thumbnail = thumbnail;
                item.IsThumbnailLoading = false;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_thumbnailCancellation, lifetime))
            {
                foreach (var item in pending) item.IsThumbnailLoading = false;
                _loadingRows = [];
            }
        }
    }

    public void CancelLoading()
    {
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = null;
        foreach (var item in _loadingRows) item.IsThumbnailLoading = false;
        _loadingRows = [];
    }

    public void Dispose()
    {
        CancelLoading();
        foreach (var item in Items) item.Dispose();
        Items.Clear();
        GC.SuppressFinalize(this);
    }


}
