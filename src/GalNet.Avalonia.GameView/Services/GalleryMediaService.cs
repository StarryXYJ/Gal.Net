using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using GalNet.Core.Gallery;
using LibVLCSharp.Shared;
using LibVlcMedia = LibVLCSharp.Shared.Media;

namespace GalNet.Avalonia.GameView.Services;

/// <summary>Scoped media session used by the default Gallery pages.</summary>
public sealed class GalleryMediaService(IGameSessionService session) : IDisposable
{
    private LibVLC? _libVlc;
    private MediaPlayer? _audioPlayer;
    private MediaPlayer? _videoPlayer;
    private bool _initializationAttempted;

    public event Action? PlaybackChanged;

    public string? ActiveAudioItemId { get; private set; }
    public bool IsAudioPlaying => _audioPlayer?.IsPlaying == true;
    public long AudioPosition => _audioPlayer?.Time ?? 0;
    public long AudioDuration => Math.Max(0, _audioPlayer?.Length ?? 0);
    public MediaPlayer? VideoPlayer => _videoPlayer;
    public string? LastError { get; private set; }

    public async Task<Bitmap?> LoadThumbnailAsync(
        GalleryItemData item,
        string resourceTypeName,
        CancellationToken cancellationToken = default)
    {
        if (!item.IsUnlocked) return null;
        var path = ResolvePath(item.Item.ResourceId);
        if (path is null) return null;

        try
        {
            if (string.Equals(resourceTypeName, "sprite", StringComparison.OrdinalIgnoreCase)
                || string.Equals(resourceTypeName, "image", StringComparison.OrdinalIgnoreCase))
                return await Task.Run(() => new Bitmap(path), cancellationToken);

            if (string.Equals(resourceTypeName, "video", StringComparison.OrdinalIgnoreCase))
            {
                var thumbnailPath = await EnsureVideoThumbnailAsync(path, cancellationToken);
                return thumbnailPath is null ? null : await Task.Run(() => new Bitmap(thumbnailPath), cancellationToken);
            }
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
            if (string.Equals(ActiveAudioItemId, item.Item.Id, StringComparison.Ordinal))
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
            _videoPlayer ??= new MediaPlayer(GetLibVlc());
            _videoPlayer.Stop();
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

    public void StopVideo() => _videoPlayer?.Stop();

    public string? ResolvePath(string resourceId) =>
        (session as IGameGallerySession)?.GalleryResources?.ResolvePath(resourceId);

    public void Dispose()
    {
        _audioPlayer?.Stop();
        _videoPlayer?.Stop();
        _audioPlayer?.Dispose();
        _videoPlayer?.Dispose();
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
        if (!_initializationAttempted)
        {
            _initializationAttempted = true;
            LibVLCSharp.Shared.Core.Initialize();
        }
        return _libVlc = new LibVLC("--no-video-title-show");
    }

    private async Task<string?> EnsureVideoThumbnailAsync(string sourcePath, CancellationToken cancellationToken)
    {
        var cacheDirectory = Path.Combine(Path.GetTempPath(), "GalNet", "GalleryThumbnails");
        Directory.CreateDirectory(cacheDirectory);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourcePath))).ToLowerInvariant();
        var thumbnailPath = Path.Combine(cacheDirectory, $"{key}.png");
        if (File.Exists(thumbnailPath)) return thumbnailPath;

        try
        {
            using var player = new MediaPlayer(GetLibVlc());
            using var media = new LibVlcMedia(GetLibVlc(), sourcePath);
            var playing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            player.Playing += (_, _) => playing.TrySetResult();
            if (!player.Play(media)) return null;
            await playing.Task.WaitAsync(TimeSpan.FromSeconds(4), cancellationToken);
            await Task.Delay(250, cancellationToken);
            if (!player.TakeSnapshot(0, thumbnailPath, 0, 0)) return null;
            player.Stop();
            return File.Exists(thumbnailPath) ? thumbnailPath : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LastError = exception.Message;
            return null;
        }
    }
}
