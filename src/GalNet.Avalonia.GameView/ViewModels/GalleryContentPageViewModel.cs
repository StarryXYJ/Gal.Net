using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Core.Gallery;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed partial class GalleryContentPageViewModel(
    IGameNavigationService navigation,
    GalleryMediaService media) : PageViewModelBase, IActivatablePageViewModel<GalleryTypeData>, IDisposable
{
    private GalleryTypeData? _type;
    private CancellationTokenSource? _thumbnailCancellation;

    public ObservableCollection<GalleryEntryViewModel> Items { get; } = [];
    public GalleryTypeData? GalleryType => _type;
    public string Title => _type?.Type.TypeId ?? "Gallery";
    public bool IsImageGrid => Renderer == GalleryRendererKind.Image;
    public bool IsVideoGrid => Renderer == GalleryRendererKind.Video;
    public bool IsVisualGrid => IsImageGrid || IsVideoGrid;
    public bool IsAudioList => Renderer == GalleryRendererKind.Audio;
    public bool IsUnsupported => Renderer == GalleryRendererKind.Unsupported;
    public string UnsupportedMessage => _type is null
        ? "This Gallery type has no renderer."
        : $"No Avalonia Gallery renderer is registered for '{_type.Type.TypeId}' ({_type.Type.ResourceTypeName}).";

    [ObservableProperty] private GalleryRendererKind _renderer;

    public Task ActivateAsync(GalleryTypeData args, CancellationToken cancellationToken = default)
    {
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        media.PlaybackChanged -= OnPlaybackChanged;
        media.PlaybackChanged += OnPlaybackChanged;
        foreach (var item in Items) item.Dispose();
        Items.Clear();
        _type = args;
        Renderer = GalleryRendererResolver.Resolve(args.Type.TypeId, args.Type.ResourceTypeName);
        OnPropertyChanged(nameof(GalleryType));
        OnPropertyChanged(nameof(Title));
        NotifyRendererProperties();

        foreach (var data in args.Items.OrderBy(item => item.Item.SortOrder ?? int.MaxValue).ThenBy(item => item.Item.Id, StringComparer.Ordinal))
            Items.Add(new GalleryEntryViewModel(data));

        if (IsVisualGrid)
            _ = LoadThumbnailsAsync(args.Type.ResourceTypeName, _thumbnailCancellation.Token);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task OpenItemAsync(GalleryEntryViewModel item, CancellationToken cancellationToken)
    {
        if (!item.IsUnlocked || _type is null) return;
        if (IsAudioList)
        {
            media.ToggleAudio(item.Data);
            UpdateAudioState();
            return;
        }
        if (!IsVisualGrid) return;
        await navigation.NavigateAsync<GalleryMediaPageViewModel, GalleryMediaPageArgs>(
            new GalleryMediaPageArgs(item.Data, Renderer), cancellationToken);
    }

    [RelayCommand] private void Back() => navigation.GoBack();

    public void Dispose()
    {
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = null;
        media.PlaybackChanged -= OnPlaybackChanged;
        foreach (var item in Items) item.Dispose();
        Items.Clear();
    }

    private async Task LoadThumbnailsAsync(string resourceTypeName, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var item in Items.Where(item => item.IsUnlocked))
            {
                var thumbnail = await media.LoadThumbnailAsync(item.Data, resourceTypeName, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    thumbnail?.Dispose();
                    break;
                }
                item.Thumbnail = thumbnail;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void OnPlaybackChanged()
    {
        if (Dispatcher.UIThread.CheckAccess()) UpdateAudioState();
        else Dispatcher.UIThread.Post(UpdateAudioState);
    }

    private void UpdateAudioState()
    {
        var duration = media.AudioDuration;
        var position = media.AudioPosition;
        foreach (var item in Items)
        {
            var active = string.Equals(item.Id, media.ActiveAudioItemId, StringComparison.Ordinal);
            item.IsPlaying = active && media.IsAudioPlaying;
            item.Progress = active && duration > 0 ? Math.Clamp(position * 100d / duration, 0, 100) : 0;
            item.TimeText = active ? $"{FormatTime(position)} / {FormatTime(duration)}" : "00:00 / 00:00";
        }
    }

    private static string FormatTime(long milliseconds) =>
        TimeSpan.FromMilliseconds(Math.Max(0, milliseconds)).ToString(@"mm\:ss", CultureInfo.InvariantCulture);

    private void NotifyRendererProperties()
    {
        OnPropertyChanged(nameof(IsImageGrid));
        OnPropertyChanged(nameof(IsVideoGrid));
        OnPropertyChanged(nameof(IsVisualGrid));
        OnPropertyChanged(nameof(IsAudioList));
        OnPropertyChanged(nameof(IsUnsupported));
        OnPropertyChanged(nameof(UnsupportedMessage));
    }
}
