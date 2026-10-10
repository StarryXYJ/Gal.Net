using System.Runtime.InteropServices;
using Avalonia;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Assets;
using GalNet.Presentation.Abstractions.View;
using GalNet.Rendering.Scene;
using LibVLCSharp.Shared;
using SkiaSharp;
using LibVlcMedia = LibVLCSharp.Shared.Media;

namespace GalNet.Avalonia.GameView.Services;

/// <summary>LibVLC pixel output composed through the same scene pipeline as image layers.</summary>
public sealed class AvaloniaVideoPresenter : IVideoPresenter, IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Playback> _playbacks = new(StringComparer.Ordinal);
    private readonly IAssetManager _assets;
    private readonly GamePageViewModel _state;
    private readonly IAvaloniaUiDispatcher _dispatcher;
    private readonly LibVLC? _vlc;
    private readonly string? _initializationError;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    private int _activeCount;
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _releaseTask;

    public AvaloniaVideoPresenter(IAssetManager assets, GamePageViewModel state, IAvaloniaUiDispatcher? dispatcher = null)
    {
        _assets = assets;
        _state = state;
        _dispatcher = dispatcher ?? AvaloniaUiDispatcher.Instance;
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            _vlc = new LibVLC("--no-video-title-show", "--avcodec-hw=none");
        }
        catch (Exception error) { _initializationError = error.Message; }
    }

    public async Task PlayVideoAsync(VideoRenderRequest request, CancellationToken cancellationToken)
    {
        var playback = new Playback(request, _dispatcher);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_playbacks.TryGetValue(request.Layer.HandleId, out var previous)) previous.Complete();
            _playbacks[request.Layer.HandleId] = playback;
            _activeCount++;
        }
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token, playback.StopSource.Token);
        using var cancellation = scope.Token.Register(() => playback.End.TrySetCanceled(scope.Token));
        var directory = Path.Combine(Path.GetTempPath(), "GalNet", "VideoLayers", Guid.NewGuid().ToString("N"));
        try
        {
            await _dispatcher.InvokeAsync(() =>
            {
                lock (_gate)
                {
                    if (!playback.End.Task.IsCompleted && _playbacks.GetValueOrDefault(request.Layer.HandleId) == playback)
                    {
                        _state.SetLayer(request.Layer.HandleId, playback.Item);
                        playback.Item = _state.Layers.First(layer => layer.HandleId == request.Layer.HandleId);
                    }
                }
                return Task.CompletedTask;
            }).ConfigureAwait(false);
            if (_vlc is null) throw new InvalidOperationException(_initializationError ?? "LibVLC video is unavailable.");
            var file = await _assets.GetFileAsync(request.Layer.AssetId, scope.Token).ConfigureAwait(false)
                ?? throw new FileNotFoundException($"Video asset '{request.Layer.AssetId}' was not found.");
            if (!string.Equals(file.TypeId, "video", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("video.play requires a video resource.");
            using var handle = await _assets.AcquireAsync<byte[]>(request.Layer.AssetId, scope.Token).ConfigureAwait(false)
                ?? throw new InvalidDataException("Video asset bytes could not be acquired.");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "media" + Path.GetExtension(file.Path));
            await File.WriteAllBytesAsync(path, handle.Value, scope.Token).ConfigureAwait(false);
            if (playback.End.Task.IsCompleted) return;
            playback.Initialize(_vlc, path);
            if (!playback.End.Task.IsCompleted && !await Task.Run(() => playback.Player!.Play(), scope.Token).ConfigureAwait(false))
                throw new InvalidOperationException("LibVLC rejected the video.");
            await playback.End.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (playback.End.Task.IsCompleted)
        {
            await playback.End.Task.ConfigureAwait(false);
        }
        finally
        {
            playback.Complete();
            try
            {
                // Native events only signal End. Stop/Dispose must run outside the native callback.
                await Task.Run(playback.Dispose, CancellationToken.None).ConfigureAwait(false);
                await _dispatcher.InvokeAsync(() =>
                {
                    var item = _state.Layers.FirstOrDefault(layer => layer.HandleId == request.Layer.HandleId);
                    if (ReferenceEquals(item?.FrameSource, playback.Frames)) _state.HideLayer(request.Layer.HandleId);
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
            finally
            {
                lock (_gate)
                {
                    if (_playbacks.GetValueOrDefault(request.Layer.HandleId) == playback)
                        _playbacks.Remove(request.Layer.HandleId);
                    if (--_activeCount == 0 && _disposed) _drained.TrySetResult();
                }
            }
        }
    }

    public void AdvanceVideo(string playbackId)
    {
        lock (_gate)
        {
            var playback = _playbacks.Values.FirstOrDefault(item => item.Request.PlaybackId == playbackId);
            if (playback is null || playback.End.Task.IsCompleted) return;
            if (playback.Request.SkipPoints.Count == 0) { playback.Complete(); return; }
            var player = playback.Player;
            if (player is null || !player.IsPlaying) return;
            var target = playback.Cursor.Next(Math.Max(0, player.Time) / 1000d, player.Length / 1000d);
            if (target is null || target > long.MaxValue / 1000d) { playback.Complete(); return; }
            if (!player.IsSeekable)
            {
                playback.Fail(new InvalidOperationException("This video backend cannot seek."));
                return;
            }
            player.Time = checked((long)(target.Value * 1000));
        }
    }

    public void StopVideo(string handleId)
    {
        lock (_gate)
            if (_playbacks.TryGetValue(handleId, out var playback)) playback.Complete();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            if (_activeCount == 0) _drained.TrySetResult();
            _releaseTask = ReleaseAsync();
        }
        // Each running async scope tears down its player before releasing its LibVLC reference.
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return new ValueTask(_releaseTask ?? Task.CompletedTask);
    }

    private async Task ReleaseAsync()
    {
        await _drained.Task.ConfigureAwait(false);
        _vlc?.Dispose();
        _lifetime.Dispose();
    }

    private sealed class Playback : IDisposable
    {
        private readonly IAvaloniaUiDispatcher _dispatcher;
        private readonly object _endGate = new();
        private IntPtr _allocation;
        private IntPtr _pixels;
        private int _width;
        private int _height;
        private int _pitch;
        private int _notificationPending;
        private LibVlcMedia? _media;
        public VideoRenderRequest Request { get; }
        public VideoSkipCursor Cursor { get; }
        public SceneFrameSource Frames { get; } = new();
        public TaskCompletionSource End { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenSource StopSource { get; } = new();

        public void Complete()
        {
            lock (_endGate)
                if (End.TrySetResult()) StopSource.Cancel();
        }

        public void Fail(Exception error)
        {
            lock (_endGate)
                if (End.TrySetException(error)) StopSource.Cancel();
        }
        public MediaPlayer? Player { get; private set; }
        public SceneLayerItem Item { get; set; }

        public Playback(VideoRenderRequest request, IAvaloniaUiDispatcher dispatcher)
        {
            Request = request;
            Cursor = new VideoSkipCursor(request.SkipPoints);
            _dispatcher = dispatcher;
            var layer = request.Layer;
            Item = new SceneLayerItem
            {
                HandleId = layer.HandleId,
                FrameSource = Frames,
                X = layer.Transform.X,
                Y = layer.Transform.Y,
                RotationDegrees = layer.Transform.RotationDegrees,
                ScaleX = layer.Transform.ScaleX,
                ScaleY = layer.Transform.ScaleY,
                Z = layer.Z,
                Opacity = layer.Opacity,
                DisplayMode = layer.DisplayMode
            };
            Frames.Disposed += Complete;
            Frames.FrameChanged += OnFrameChanged;
        }

        public void Initialize(LibVLC vlc, string path)
        {
            _media = new LibVlcMedia(vlc, path, FromType.FromPath);
            Player = new MediaPlayer(_media);
            Player.EndReached += (_, _) => Complete();
            Player.EncounteredError += (_, _) => Fail(new InvalidOperationException("LibVLC could not decode the video."));
            Player.SetVideoFormatCallbacks(Format, Cleanup);
            Player.SetVideoCallbacks(Lock, null, Display);
        }

        private uint Format(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
        {
            try
            {
                FreeBuffer();
                if (width == 0 || height == 0 || width > 8192 || height > 8192) return 0;
                _width = checked((int)width);
                _height = checked((int)height);
                _pitch = checked((_width * 4 + 31) & ~31);
                pitches = (uint)_pitch;
                lines = (height + 31) & ~31u;
                Marshal.Copy(new byte[] { (byte)'R', (byte)'V', (byte)'3', (byte)'2' }, 0, chroma, 4);
                _allocation = Marshal.AllocHGlobal(checked(_pitch * (int)lines + 31));
                _pixels = new IntPtr((_allocation.ToInt64() + 31) & ~31L);
                return 1;
            }
            catch (Exception error) { Fail(error); return 0; }
        }

        private IntPtr Lock(IntPtr opaque, IntPtr planes)
        {
            Marshal.WriteIntPtr(planes, _pixels);
            return IntPtr.Zero;
        }

        private void Display(IntPtr opaque, IntPtr picture)
        {
            if (End.Task.IsCompleted || _pixels == IntPtr.Zero) return;
            try
            {
                using var decoded = new SKBitmap();
                decoded.InstallPixels(new SKImageInfo(_width, _height, SKColorType.Bgra8888, SKAlphaType.Opaque), _pixels, _pitch);
                Frames.Publish(decoded.Copy());
            }
            catch (Exception error) { Fail(error); }
        }

        private void OnFrameChanged()
        {
            if (Interlocked.Exchange(ref _notificationPending, 1) != 0) return;
            _dispatcher.Dispatch(() =>
            {
                Interlocked.Exchange(ref _notificationPending, 0);
                if (!End.Task.IsCompleted) Item.NotifyFrameChanged();
            });
        }

        private void Cleanup(ref IntPtr opaque) => FreeBuffer();
        private void FreeBuffer()
        {
            _pixels = IntPtr.Zero;
            if (_allocation != IntPtr.Zero) Marshal.FreeHGlobal(_allocation);
            _allocation = IntPtr.Zero;
        }

        public void Dispose()
        {
            Player?.Stop();
            Player?.Dispose();
            _media?.Dispose();
            Frames.Dispose();
            FreeBuffer();
            lock (_endGate) StopSource.Dispose();
        }
    }
}
