using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Services;
using LibVLCSharp.Shared;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed partial class GalleryMediaPageViewModel(
    IGameNavigationService navigation,
    GalleryMediaService media) : PageViewModelBase, IActivatablePageViewModel<GalleryMediaPageArgs>, IDisposable
{
    public MediaPlayer? VideoPlayer => media.VideoPlayer;
    public bool IsImage { get; private set; }
    public bool IsVideo { get; private set; }

    [ObservableProperty] private string _title = "Gallery";
    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private string? _errorMessage;

    public Task ActivateAsync(GalleryMediaPageArgs args, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Image?.Dispose();
        Image = null;
        ErrorMessage = null;
        Title = string.IsNullOrWhiteSpace(args.Item.Item.Title) ? args.Item.Item.Id : args.Item.Item.Title;
        if (!args.Item.IsUnlocked)
        {
            IsImage = false;
            IsVideo = false;
            OnPropertyChanged(nameof(IsImage));
            OnPropertyChanged(nameof(IsVideo));
            ErrorMessage = "This Gallery item is locked.";
            return Task.CompletedTask;
        }
        IsImage = args.Renderer == GalleryRendererKind.Image;
        IsVideo = args.Renderer == GalleryRendererKind.Video;
        OnPropertyChanged(nameof(IsImage));
        OnPropertyChanged(nameof(IsVideo));

        if (IsImage)
        {
            var path = media.ResolvePath(args.Item.Item.ResourceId);
            try { Image = path is null ? null : new Bitmap(path); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                ErrorMessage = exception.Message;
            }
            if (Image is null && ErrorMessage is null) ErrorMessage = "The Gallery resource is unavailable.";
        }
        else if (IsVideo)
        {
            media.PlayVideo(args.Item);
            OnPropertyChanged(nameof(VideoPlayer));
            ErrorMessage = media.LastError;
        }
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void Back()
    {
        media.StopVideo();
        navigation.GoBack();
    }

    public void Dispose()
    {
        media.StopVideo();
        Image?.Dispose();
        Image = null;
    }
}
