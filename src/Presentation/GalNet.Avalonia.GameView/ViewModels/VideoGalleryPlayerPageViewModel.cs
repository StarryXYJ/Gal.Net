using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Rendering.Scene;
using LibVLCSharp.Shared;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed partial class VideoGalleryPlayerPageViewModel : BackNavigablePageViewModel, IActivatablePageViewModel<GalleryItemData>, IDisposable
{
    private readonly GalleryMediaService _media;
    private readonly DispatcherTimer _timer;
    private bool _updating;
    private bool _seeking;
    public MediaPlayer? VideoPlayer => _media.VideoPlayer;
    public IReadOnlyList<SceneLayerItem> VideoLayers { get; }
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _hasVideo;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _canSeek;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _timeText = "00:00 / 00:00";

    public VideoGalleryPlayerPageViewModel(IGameNavigationService navigation, GalleryMediaService media) : base(navigation)
    {
        _media = media;
        VideoLayers = [media.VideoLayer];
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += OnTick;
    }

    public Task ActivateAsync(GalleryItemData item, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stop();
        Title = item.IsUnlocked ? _media.ResolveName(item.Item) : "???";
        ErrorMessage = item.IsUnlocked ? null : _media.ResolveText("Gallery.Locked", "Locked");
        if (item.IsUnlocked)
        {
            _media.PlayVideo(item);
            UpdatePlayback();
            if (HasVideo) _timer.Start();
        }
        OnPropertyChanged(nameof(VideoPlayer));
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void TogglePlayback() { _media.ToggleVideoPlayback(); UpdatePlayback(); }

    public void BeginSeek() => _seeking = CanSeek;
    public void EndSeek()
    {
        if (!_seeking) return;
        _seeking = false;
        Seek();
    }
    partial void OnProgressChanged(double value) { if (!_updating && !_seeking) Seek(); }
    private void Seek() { if (CanSeek) _media.SeekVideo((long)(_media.VideoDuration * Math.Clamp(Progress, 0, 100) / 100)); }
    private void OnTick(object? sender, EventArgs args) => UpdatePlayback();
    private void UpdatePlayback()
    {
        ErrorMessage = _media.LastError;
        HasVideo = _media.HasVideo;
        IsPlaying = _media.IsVideoPlaying;
        var duration = _media.VideoDuration;
        CanSeek = HasVideo && _media.CanSeekVideo && duration > 0;
        if (!_seeking || !CanSeek)
        {
            _seeking = false;
            _updating = true;
            try { Progress = duration > 0 ? Math.Clamp(_media.VideoPosition * 100d / duration, 0, 100) : 0; }
            finally { _updating = false; }
        }
        TimeText = $"{FormatTime(_media.VideoPosition)} / {FormatTime(duration)}";
    }
    private static string FormatTime(long milliseconds) => TimeSpan.FromMilliseconds(milliseconds).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    private void Stop()
    {
        _timer.Stop();
        _seeking = false;
        _media.StopVideo();
        UpdatePlayback();
    }
    protected override void OnBack() { Stop(); base.OnBack(); }
    public void Dispose() { Stop(); _timer.Tick -= OnTick; }
}
