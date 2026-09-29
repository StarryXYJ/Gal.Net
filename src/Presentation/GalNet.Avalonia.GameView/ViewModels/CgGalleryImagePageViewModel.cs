using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Core.Gallery;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed partial class CgGalleryImagePageViewModel(
    IGameNavigationService navigation,
    GalleryMediaService media) : PageViewModelBase, IActivatablePageViewModel<GalleryItemData>, IDisposable
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
        Title = string.IsNullOrWhiteSpace(item.Item.Title) ? item.Item.Id.ToString() : item.Item.Title;
        if (!item.IsUnlocked) { ErrorMessage = "This Gallery item is locked."; return Task.CompletedTask; }
        var path = media.ResolvePath(item.Item.ResourceId);
        try { Image = path is null ? null : new Bitmap(path); }
        catch (Exception exception) { ErrorMessage = exception.Message; }
        if (Image is null && ErrorMessage is null) ErrorMessage = "The Gallery resource is unavailable.";
        return Task.CompletedTask;
    }

    [RelayCommand] private void Back() => navigation.GoBack();
    public void Dispose() { Image?.Dispose(); Image = null; }
}
