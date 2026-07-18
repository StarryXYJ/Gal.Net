using Avalonia.Threading;
using GalNet.Control.Screen.Game;
using GalNet.Core.Assets;
using LibVLCSharp.Shared;
using Serilog;

namespace GalNet.Control.Runtime.Presentation;

public sealed class VideoController : IDisposable
{
    private readonly LibVLC? _libVlc;
    private readonly MediaPlayer? _videoPlayer;
    private readonly bool _vlcInitialized;
    private readonly GameScreenView _gameScreen;
    private readonly IAssetManager? _assets;
    private Media? _media;
    private string? _temporaryFilePath;
    private int _generation;
    private int _disposed;
    private bool _cleaningUp;

    public VideoController(LibVLC? libVlc, MediaPlayer? videoPlayer, bool vlcInitialized, GameScreenView gameScreen, IAssetManager? assets)
    {
        _libVlc = libVlc;
        _videoPlayer = videoPlayer;
        _vlcInitialized = vlcInitialized;
        _gameScreen = gameScreen;
        _assets = assets;
        if (_videoPlayer is not null)
        {
            _videoPlayer.EnableKeyInput = false;
            _videoPlayer.EnableMouseInput = false;
            _videoPlayer.EndReached += OnPlaybackEnded;
            _videoPlayer.EncounteredError += OnPlaybackError;
        }
    }

    public void Play(string assetId)
    {
        var player = _videoPlayer;
        if (!_vlcInitialized || _libVlc is null || player is null || Volatile.Read(ref _disposed) != 0)
            return;
        var generation = Interlocked.Increment(ref _generation);
        Dispatcher.UIThread.Post(() =>
        {
            if (generation == Volatile.Read(ref _generation))
                CleanupCurrent(stopPlayer: true);
        });
        _ = PlayAsync(assetId, generation, player);
    }

    private async Task PlayAsync(string assetId, int generation, MediaPlayer player)
    {
        Media? media = null;
        string? temporaryFilePath = null;
        try
        {
            if (_assets is not null)
            {
                var file = await _assets.GetFileAsync(assetId);
                if (file is null)
                {
                    Log.ForContext("LogChannel", "Game").Warning("Video asset was not found: {AssetId}", assetId);
                    return;
                }
                // LibVLC 3 implements StreamMediaInput through imem://, which emits an
                // invalid get/release callback error even when playback later succeeds.
                // A scoped temporary file gives VLC a normal seekable file input instead.
                temporaryFilePath = CreateTemporaryVideoPath(file.Path);
                await using (var source = file.OpenRead())
                await using (var destination = new FileStream(
                    temporaryFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
                    bufferSize: 81920, options: FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await source.CopyToAsync(destination);
                }
                media = new Media(_libVlc!, new Uri(temporaryFilePath));
            }
            else if (File.Exists(assetId))
                media = new Media(_libVlc!, new Uri(Path.GetFullPath(assetId)));
            else
            {
                Log.ForContext("LogChannel", "Game").Warning("Video asset was not found: {AssetId}", assetId);
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation != Volatile.Read(ref _generation) || Volatile.Read(ref _disposed) != 0)
                    return;
                CleanupCurrent(stopPlayer: true);
                _media = media; media = null;
                _temporaryFilePath = temporaryFilePath; temporaryFilePath = null;
                _gameScreen.VideoView.MediaPlayer = player;
                _gameScreen.VideoView.IsVisible = true;
                player.Media = _media;
                if (!player.Play())
                {
                    Log.ForContext("LogChannel", "Game").Warning("VLC refused to play video asset: {AssetId}", assetId);
                    CleanupCurrent(stopPlayer: false);
                }
            });
        }
        catch (Exception exception)
        {
            Log.ForContext("LogChannel", "Game").Warning(exception, "Failed to play video asset: {AssetId}", assetId);
            Dispatcher.UIThread.Post(() =>
            {
                if (generation == Volatile.Read(ref _generation))
                    CleanupCurrent(stopPlayer: true);
            });
        }
        finally
        {
            media?.Dispose();
            DeleteTemporaryFile(temporaryFilePath);
        }
    }

    public void Stop()
    {
        Interlocked.Increment(ref _generation);
        if (Dispatcher.UIThread.CheckAccess()) CleanupCurrent(stopPlayer: true);
        else Dispatcher.UIThread.Post(() => CleanupCurrent(stopPlayer: true));
    }

    private void OnPlaybackEnded(object? sender, EventArgs e) => Stop();

    private void OnPlaybackError(object? sender, EventArgs e)
    {
        Log.ForContext("LogChannel", "Game").Warning("VLC encountered an error while playing video");
        Stop();
    }

    private void CleanupCurrent(bool stopPlayer)
    {
        if (_cleaningUp) return;
        _cleaningUp = true;
        try
        {
            if (stopPlayer) _videoPlayer?.Stop();
            if (_videoPlayer is not null) _videoPlayer.Media = null;
            _gameScreen.VideoView.IsVisible = false;
            _media?.Dispose();
            _media = null;
            DeleteTemporaryFile(_temporaryFilePath);
            _temporaryFilePath = null;
        }
        finally { _cleaningUp = false; }
    }

    private static string CreateTemporaryVideoPath(string originalPath)
    {
        var directory = Path.Combine(Path.GetTempPath(), "GalNet", "video");
        Directory.CreateDirectory(directory);
        var extension = Path.GetExtension(originalPath);
        if (extension.Length > 16 || extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            extension = "";
        return Path.Combine(directory, $"{Guid.NewGuid():N}{extension}");
    }

    private static void DeleteTemporaryFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { File.Delete(path); }
        catch (Exception exception)
        {
            Log.ForContext("LogChannel", "Game").Debug(exception, "Could not delete temporary video file: {Path}", path);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Interlocked.Increment(ref _generation);
        void DisposeOnUiThread()
        {
            CleanupCurrent(stopPlayer: true);
            if (_videoPlayer is not null)
            {
                _videoPlayer.EndReached -= OnPlaybackEnded;
                _videoPlayer.EncounteredError -= OnPlaybackError;
                _gameScreen.VideoView.MediaPlayer = null;
                _videoPlayer.Dispose();
            }
        }
        if (Dispatcher.UIThread.CheckAccess()) DisposeOnUiThread();
        else Dispatcher.UIThread.InvokeAsync(DisposeOnUiThread).GetAwaiter().GetResult();
    }
}
