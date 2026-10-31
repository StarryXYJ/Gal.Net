using Avalonia.Media.Imaging;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Media.LibVlc;
using LibVLCSharp.Shared;
using LibVlcMedia = LibVLCSharp.Shared.Media;
using GalNet.Rendering.Scene;
using GalNet.Contracts.Builtins.Scene;

namespace GalNet.Avalonia.GameView.Services;

/// <summary>Scoped media session used by the default Gallery pages.</summary>
public sealed class GalleryMediaService(IGameSessionService session, LibVlcFactory? mediaFactory = null, GalNet.Core.Services.ITextResolver? text = null) : IDisposable
{
    private LibVLC? _libVlc;
    private MediaPlayer? _audioPlayer;
    private MediaPlayer? _videoPlayer;
    private LibVlcFrameOutput? _videoFrames;
    private int _notificationPending;
    private readonly Dictionary<int, AudioPreviewState> _audioStates = [];
    private readonly GalleryAudioDurationService _audioDurations = new(mediaFactory ?? LibVlcFactory.Default);
    private double? _pendingAudioSeek;
    private bool _audioSeekIssued;
    private bool _disposed;
    private sealed class AudioPreviewState(string resourceId)
    {
        public string ResourceId { get; } = resourceId;
        public long Position;
        public long Duration;
        public double Progress;
    }
    public readonly record struct AudioPreviewSnapshot(long Position, long Duration, double Progress);
    public SceneLayerItem VideoLayer { get; } = new() { HandleId = "gallery-video", DisplayMode = LayerDisplayMode.Uniform };
    private readonly LibVlcFactory _factory = mediaFactory ?? LibVlcFactory.Default;
    private readonly GalleryThumbnailService _thumbnails = new(mediaFactory ?? LibVlcFactory.Default);

    public string ResolveName(GalleryItem item) => string.IsNullOrWhiteSpace(item.DisplayNameKey) ? "" : text?.Resolve(item.DisplayNameKey) ?? "";
    public string ResolveText(string key, string fallback)
    {
        var value = text?.Resolve(key); return string.IsNullOrEmpty(value) || value == key ? fallback : value;
    }
    public bool CanSeekAudio => _audioPlayer?.IsSeekable == true;
    public event Action? PlaybackChanged;

    public int? ActiveAudioItemId { get; private set; }
    public bool IsAudioPlaying => _audioPlayer?.IsPlaying == true;
    public long AudioPosition => _audioPlayer?.Time ?? 0;
    public long AudioDuration => Math.Max(0, _audioPlayer?.Length ?? 0);
    public bool HasVideo => _videoPlayer is not null;
    public bool IsVideoPlaying => _videoPlayer?.IsPlaying == true;
    public bool CanSeekVideo => _videoPlayer?.IsSeekable == true;
    public long VideoPosition => Math.Max(0, _videoPlayer?.Time ?? 0);
    public long VideoDuration => Math.Max(0, _videoPlayer?.Length ?? 0);
    public MediaPlayer? VideoPlayer => _videoPlayer;
    public string? LastError { get; private set; }

    public Task<Bitmap?> LoadImageThumbnailAsync(GalleryItemData item, CancellationToken cancellationToken = default) =>
        LoadThumbnailAsync(item, false, cancellationToken);

    public Task<Bitmap?> LoadSceneThumbnailAsync(GalleryItemData item, CancellationToken cancellationToken = default) =>
        item.Item.PreviewResourceId is { } preview ? LoadImageThumbnailAsync(item with { Item = item.Item with { ResourceId = preview } }, cancellationToken) : Task.FromResult<Bitmap?>(null);

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

    private AudioPreviewState AudioState(GalleryItemData item)
    {
        if (!_audioStates.TryGetValue(item.Item.Id, out var state) || state.ResourceId != item.Item.ResourceId)
            _audioStates[item.Item.Id] = state = new(item.Item.ResourceId);
        return state;
    }
    public async Task PrepareAudioAsync(GalleryItemData item, CancellationToken cancellationToken = default)
    {
        if (!item.IsUnlocked || ResolvePath(item.Item.ResourceId) is not { } path) return;
        var state = AudioState(item);
        try
        {
            var duration = await _audioDurations.GetAsync(path, cancellationToken);
            if (_disposed || cancellationToken.IsCancellationRequested) return;
            state.Duration = duration;
            state.Position = (long)(duration * state.Progress / 100);
            PlaybackChanged?.Invoke();
        }
        catch (Exception error) when (error is not OperationCanceledException)
        { Serilog.Log.Debug(error, "Could not read Gallery audio duration"); }
    }
    public AudioPreviewSnapshot GetAudioPreview(GalleryItemData item)
    {
        CaptureActiveAudio();
        var state = AudioState(item);
        return new(state.Position, state.Duration, state.Progress);
    }
    private void CaptureActiveAudio()
    {
        if (_audioPlayer is null || ActiveAudioItemId is not { } id || !_audioStates.TryGetValue(id, out var state)) return;
        if (_pendingAudioSeek is not null) return;
        var duration = Math.Max(0, _audioPlayer.Length);
        if (duration > 0) state.Duration = duration;
        var position = _audioPlayer.State == VLCState.Ended ? state.Duration : _audioPlayer.Time;
        if (position < 0) return;
        state.Position = position;
        if (state.Duration > 0) state.Progress = Math.Clamp(position * 100d / state.Duration, 0, 100);
    }
    public void ToggleAudio(GalleryItemData item)
    {
        if (!item.IsUnlocked) return;
        if (ActiveAudioItemId == item.Item.Id && _audioPlayer?.IsPlaying == true)
        {
            CaptureActiveAudio();
            _audioPlayer.Pause();
            PlaybackChanged?.Invoke();
            return;
        }
        StartAudio(item);
    }
    /// <summary>A seek selects and starts this track, even if it has never been played.</summary>
    public void SeekAudio(GalleryItemData item, double percent)
    {
        if (!item.IsUnlocked) return;
        StartAudio(item, Math.Clamp(percent, 0, 100));
    }
    private void StartAudio(GalleryItemData item, double? percent = null)
    {
        var path = ResolvePath(item.Item.ResourceId);
        if (path is null)
        {
            LastError = $"Gallery resource '{item.Item.ResourceId}' is unavailable.";
            PlaybackChanged?.Invoke();
            return;
        }
        try
        {
            CaptureActiveAudio();
            var state = AudioState(item);
            if (percent is { } requested) state.Progress = requested;
            else if (state.Progress >= 100) state.Progress = 0;
            state.Position = (long)(state.Duration * state.Progress / 100);
            var player = GetAudioPlayer();
            if (ActiveAudioItemId != item.Item.Id || player.State == VLCState.Ended)
            {
                player.Stop();
                ActiveAudioItemId = null;
                _pendingAudioSeek = state.Progress; _audioSeekIssued = false;
                using var media = new LibVlcMedia(GetLibVlc(), path);
                if (!player.Play(media)) throw new InvalidOperationException($"Unable to play Gallery audio '{item.Item.ResourceId}'.");
                ActiveAudioItemId = item.Item.Id;
            }
            else
            {
                if (percent is not null) { _pendingAudioSeek = state.Progress; _audioSeekIssued = false; }
                if (!player.IsPlaying && !player.Play()) throw new InvalidOperationException("Unable to resume Gallery audio.");
            }
            ApplyPendingAudioSeek();
            LastError = null;
        }
        catch (Exception error) { LastError = error.Message; }
        PlaybackChanged?.Invoke();
    }
    private void ApplyPendingAudioSeek()
    {
        if (_pendingAudioSeek is not { } percent || _audioPlayer?.IsSeekable != true || _audioPlayer.Length <= 0 ||
            _audioPlayer.State is not (VLCState.Playing or VLCState.Paused)) return;
        var target = (long)(_audioPlayer.Length * percent / 100);
        if (!_audioSeekIssued) { _audioSeekIssued = true; _audioPlayer.Time = target; }
        else if (Math.Abs(_audioPlayer.Time - target) < 500) _pendingAudioSeek = null;
    }
    private void NotifyAudioChanged()
    {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            ApplyPendingAudioSeek();
            PlaybackChanged?.Invoke();
        });
    }

    public void ToggleVideoPlayback()
    {
        if (_videoPlayer is null) return;
        try
        {
            if (_videoPlayer.IsPlaying) _videoPlayer.Pause();
            else if (!_videoPlayer.Play()) throw new InvalidOperationException("Unable to resume Gallery video.");
            LastError = null;
        }
        catch (Exception error) { LastError = error.Message; }
    }

    public void SeekVideo(long milliseconds)
    {
        if (_videoPlayer?.IsSeekable != true) return;
        _videoPlayer.Time = Math.Clamp(milliseconds, 0, VideoDuration);
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
            PauseAudio();
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

    private void PauseAudio()
    {
        CaptureActiveAudio();
        _audioPlayer?.SetPause(true);
    }
    public void StopPlayback()
    {
        CaptureActiveAudio();
        _pendingAudioSeek = null;
        ActiveAudioItemId = null;
        _audioPlayer?.Stop();
        StopVideo();
        ActiveAudioItemId = null;
        PlaybackChanged?.Invoke();
    }

    public string? ResolvePath(string resourceId) =>
        (session as IGameGallerySession)?.GalleryResources?.ResolvePath(resourceId);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _audioDurations.Dispose();
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
        _audioPlayer.TimeChanged += (_, _) => NotifyAudioChanged();
        _audioPlayer.LengthChanged += (_, _) => NotifyAudioChanged();
        _audioPlayer.Playing += (_, _) => NotifyAudioChanged();
        _audioPlayer.Paused += (_, _) => NotifyAudioChanged();
        _audioPlayer.Stopped += (_, _) => NotifyAudioChanged();
        _audioPlayer.EndReached += (_, _) => NotifyAudioChanged();
        return _audioPlayer;
    }

    private LibVLC GetLibVlc()
    {
        if (_libVlc is not null) return _libVlc;
        return _libVlc = _factory.Create("--avcodec-hw=none");
    }

}
