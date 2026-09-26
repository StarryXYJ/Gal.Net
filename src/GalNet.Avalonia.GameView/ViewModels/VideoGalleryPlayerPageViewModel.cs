using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Core.Gallery;
using LibVLCSharp.Shared;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed partial class VideoGalleryPlayerPageViewModel(
    IGameNavigationService navigation,
    GalleryMediaService media) : PageViewModelBase, IActivatablePageViewModel<GalleryItemData>, IDisposable
{
    public MediaPlayer? VideoPlayer => media.VideoPlayer;
    [ObservableProperty] private string _title = "Gallery";
    [ObservableProperty] private string? _errorMessage;

    public Task ActivateAsync(GalleryItemData item, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Title = string.IsNullOrWhiteSpace(item.Item.Title) ? item.Item.Id.ToString() : item.Item.Title;
        ErrorMessage = null;
        if (!item.IsUnlocked) ErrorMessage = "This Gallery item is locked.";
        else { media.PlayVideo(item); ErrorMessage = media.LastError; }
        OnPropertyChanged(nameof(VideoPlayer));
        return Task.CompletedTask;
    }

    [RelayCommand] private void Back() { media.StopVideo(); navigation.GoBack(); }
    public void Dispose() => media.StopVideo();
}
