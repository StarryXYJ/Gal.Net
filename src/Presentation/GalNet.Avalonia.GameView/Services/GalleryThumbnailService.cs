using LibVLCSharp.Shared;
using GalNet.Media.LibVlc;
using LibVlcMedia = LibVLCSharp.Shared.Media;
using SkiaSharp;

namespace GalNet.Avalonia.GameView.Services;

/// <summary>Owns thumbnail extraction and its temporary cache independently of Gallery playback.</summary>
internal sealed class GalleryThumbnailService(LibVlcFactory mediaFactory) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GalNet", "GalleryThumbnails", Guid.NewGuid().ToString("N"));
    private LibVLC? _vlc;
    private bool _disposed;

    public async Task<string?> CreateAsync(string sourcePath, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _gate.WaitAsync(cancellation.Token).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(sourcePath, out var cached)) return cached;
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".png");
            if (_vlc is null)
            {
                _vlc = mediaFactory.Create("--avcodec-hw=none");
            }
            using var player = new MediaPlayer(_vlc);
            var decoded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var output = new LibVlcFrameOutput(player, error => decoded.TrySetException(error));
            output.Frames.FrameChanged += () => decoded.TrySetResult();
            using var media = new LibVlcMedia(_vlc, sourcePath);
            media.AddOption(":no-audio");
            player.EncounteredError += (_, _) => decoded.TrySetException(new InvalidDataException("LibVLC could not decode the Gallery thumbnail."));
            try
            {
                if (!player.Play(media)) return null;
                await decoded.Task.WaitAsync(TimeSpan.FromSeconds(4), cancellation.Token).ConfigureAwait(false);
                using var frame = output.Frames.AcquireFrame();
                if (frame.Texture?.SkBitmap is not { } bitmap) return null;
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using (var stream = File.Create(path)) data.SaveTo(stream);
                _cache.Add(sourcePath, path);
                return path;
            }
            finally { player.Stop(); }
        }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _gate.Wait();
        try
        {
            _vlc?.Dispose();
            _cache.Clear();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
        finally { _gate.Release(); }
    }
}
