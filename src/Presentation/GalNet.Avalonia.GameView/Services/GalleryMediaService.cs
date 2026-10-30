using Avalonia.Media.Imaging;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Media.LibVlc;
using LibVLCSharp.Shared;
using LibVlcMedia = LibVLCSharp.Shared.Media;
using GalNet.Rendering.Scene;
using GalNet.Contracts.Builtins.Scene;

namespace GalNet.Avalonia.GameView.Services;

/// <summary>Scoped media session used by the default Gallery pages.</summary>
public sealed class GalleryMediaService(IGameSessionService session, LibVlcFactory? mediaFactory = null) : IDisposable
{
    private LibVLC? _libVlc;
    private MediaPlayer? _audioPlayer;
    private MediaPlayer? _videoPlayer;
    private LibVlcFrameOutput? _videoFrames;
    private int _notificationPending;
    public SceneLayerItem VideoLayer { get; } = new() { HandleId = "gallery-video", DisplayMode = LayerDisplayMode.Uniform };
    private readonly LibVlcFactory _factory = mediaFactory ?? LibVlcFactory.Default;
    private readonly GalleryThumbnailService _thumbnails = new(mediaFactory ?? LibVlcFactory.Default);

    public event Action? PlaybackChanged;

    public int? ActiveAudioItemId { get; private set; }
    public bool IsAudioPlaying => _audioPlayer?.IsPlaying == true;
    public long AudioPosition => _audioPlayer?.Time ?? 0;
    public long AudioDuration => Math.Max(0, _audioPlayer?.Length ?? 0);
    public MediaPlayer? VideoPlayer => _videoPlayer;
    public string? LastError { get; private set; }

    public Task<Bitmap?> LoadImageThumbnailAsync(GalleryItemData item, CancellationToken cancellationToken = default) =>
        LoadThumbnailAsync(item, false, cancellationToken);

    public Task<Bitmap?> LoadVideoThumbnailAsync(GalleryItemData item, CancellationToken cancellationToken = default) =>
        LoadThumbnailAsync(item, true, cancellationToken);

    private async Task<Bitmap?> LoadThumbnailAsync(
        GalleryItemData item,
        bool video,
        CancellationToken cancellationToken)
    {
        if (!item.IsUnlocked) return null;
        var path = ResolvePath(item.Item.ResourceId);
        if (path is null) return null;

        try
        {
            if (video)
            {
                var thumbnailPath = await _thumbnails.CreateAsync(path, cancellationToken);
                return thumbnailPath is null ? null : await Task.Run(() => new Bitmap(thumbnailPath), cancellationToken);
            }
            return await Task.Run(() => new Bitmap(path), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LastError = exception.Message;
        }

        return null;
    }

    public void ToggleAudio(GalleryItemData item)
    {
        if (!item.IsUnlocked) return;
        var path = ResolvePath(item.Item.ResourceId);
        if (path is null)
        {
            LastError = $"Gallery resource '{item.Item.ResourceId}' is unavailable.";
            PlaybackChanged?.Invoke();
            return;
        }

        try
        {
            var player = GetAudioPlayer();
            if (ActiveAudioItemId == item.Item.Id)
            {
                player.SetPause(player.IsPlaying);
            }
            else
            {
                player.Stop();
                ActiveAudioItemId = null;
                using var media = new LibVlcMedia(GetLibVlc(), path);
                if (!player.Play(media))
                    throw new InvalidOperationException($"Unable to play Gallery audio '{item.Item.ResourceId}'.");
                ActiveAudioItemId = item.Item.Id;
            }
            LastError = null;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
        }

        PlaybackChanged?.Invoke();
    }

    public void SeekAudio(long milliseconds)
    {
        if (_audioPlayer is null) return;
        _audioPlayer.Time = Math.Clamp(milliseconds, 0, Math.Max(0, _audioPlayer.Length));
        PlaybackChanged?.Invoke();
    }

    public void PlayVideo(GalleryItemData item)
    {
        if (!item.IsUnlocked) return;
        var path = ResolvePath(item.Item.ResourceId);
        if (path is null)
        {
            LastError = $"Gallery resource '{item.Item.ResourceId}' is unavailable.";
            return;
        }

        try
        {
            _audioPlayer?.Stop();
            StopVideo();
            _videoPlayer = new MediaPlayer(GetLibVlc());
            _videoFrames = new LibVlcFrameOutput(_videoPlayer, error =>
            {
                LastError = error.Message;
                PlaybackChanged?.Invoke();
            });
            VideoLayer.FrameSource = _videoFrames.Frames;
            _videoFrames.Frames.FrameChanged += OnVideoFrame;
            using var media = new LibVlcMedia(GetLibVlc(), path);
            if (!_videoPlayer.Play(media))
                throw new InvalidOperationException($"Unable to play Gallery video '{item.Item.ResourceId}'.");
            LastError = null;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
        }
    }

    private void OnVideoFrame()
    {
        if (Interlocked.Exchange(ref _notificationPending, 1) != 0) return;
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _notificationPending, 0);
            VideoLayer.NotifyFrameChanged();
        });
    }

    public void StopVideo()
    {
        _videoPlayer?.Stop();
        _videoPlayer?.Dispose();
        _videoPlayer = null;
        if (_videoFrames is not null) _videoFrames.Frames.FrameChanged -= OnVideoFrame;
        _videoFrames?.Dispose();
        _videoFrames = null;
        VideoLayer.FrameSource = null;
    }

    public void StopPlayback()
    {
        _audioPlayer?.Stop();
        StopVideo();
        ActiveAudioItemId = null;
        PlaybackChanged?.Invoke();
    }

    public string? ResolvePath(string resourceId) =>
        (session as IGameGallerySession)?.GalleryResources?.ResolvePath(resourceId);

    public void Dispose()
    {
        _audioPlayer?.Stop();
        StopVideo();
        _audioPlayer?.Dispose();
        _thumbnails.Dispose();
        _libVlc?.Dispose();
    }

    private MediaPlayer GetAudioPlayer()
    {
        if (_audioPlayer is not null) return _audioPlayer;
        _audioPlayer = new MediaPlayer(GetLibVlc());
        _audioPlayer.TimeChanged += (_, _) => PlaybackChanged?.Invoke();
        _audioPlayer.LengthChanged += (_, _) => PlaybackChanged?.Invoke();
        _audioPlayer.Playing += (_, _) => PlaybackChanged?.Invoke();
        _audioPlayer.Paused += (_, _) => PlaybackChanged?.Invoke();
        _audioPlayer.Stopped += (_, _) => PlaybackChanged?.Invoke();
        _audioPlayer.EndReached += (_, _) =>
        {
            ActiveAudioItemId = null;
            PlaybackChanged?.Invoke();
        };
        return _audioPlayer;
    }

    private LibVLC GetLibVlc()
    {
        if (_libVlc is not null) return _libVlc;
        return _libVlc = _factory.Create("--avcodec-hw=none");
    }

}
