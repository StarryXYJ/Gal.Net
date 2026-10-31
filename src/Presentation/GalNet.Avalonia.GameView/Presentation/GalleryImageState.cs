using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Avalonia.GameView.Services;
using GalNet.Contracts.Builtins.Gallery;

namespace GalNet.Avalonia.GameView.Presentation;

/// <summary>Owns the decoded full-size Gallery image and releases it on replacement or disposal.</summary>
public sealed partial class GalleryImageState(
    GalleryMediaService media) : ObservableObject, IDisposable
{
    [ObservableProperty] private string _title = "Gallery";
    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private string? _errorMessage;

    public Task ActivateAsync(GalleryItemData item, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Image?.Dispose();
        Image = null;
        ErrorMessage = null;
        Title = item.IsUnlocked ? media.ResolveName(item.Item) : "";
        if (!item.IsUnlocked) { ErrorMessage = "This Gallery item is locked."; return Task.CompletedTask; }
        var path = media.ResolvePath(item.Item.ResourceId);
        try { Image = path is null ? null : new Bitmap(path); }
        catch (Exception exception) { ErrorMessage = exception.Message; }
        if (Image is null && ErrorMessage is null) ErrorMessage = "The Gallery resource is unavailable.";
        return Task.CompletedTask;
    }

    public void Dispose() { Image?.Dispose(); Image = null; }
}
