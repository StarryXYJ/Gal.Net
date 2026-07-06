using Avalonia.Threading;
using GalNet.Control.Screen.BuiltIn;
using LibVLCSharp.Shared;

namespace GalNet.Control.View;

/// <summary>
/// 视频子系统控制器 —— 封装 LibVLC 播放及 UI 管理，将视频逻辑从 DefaultGameView 隔离开。
/// </summary>
public sealed class VideoController
{
    private readonly LibVLC? _libVlc;
    private readonly MediaPlayer? _videoPlayer;
    private readonly bool _vlcInitialized;
    private readonly GameScreenView _gameScreen;

    public VideoController(LibVLC? libVlc, MediaPlayer? videoPlayer, bool vlcInitialized, GameScreenView gameScreen)
    {
        _libVlc = libVlc;
        _videoPlayer = videoPlayer;
        _vlcInitialized = vlcInitialized;
        _gameScreen = gameScreen;
    }

    public void Play(string assetId)
    {
        if (!_vlcInitialized || _libVlc == null || _videoPlayer == null) return;
        Dispatcher.UIThread.Post(() =>
        {
            _gameScreen.VideoView.IsVisible = true;
            _gameScreen.VideoView.MediaPlayer = _videoPlayer;
            _videoPlayer.Media = new Media(_libVlc, assetId);
            _videoPlayer.Play();
        });
    }

    public void Stop()
    {
        if (!_vlcInitialized || _videoPlayer == null) return;
        _videoPlayer.Stop();
        Dispatcher.UIThread.Post(() => _gameScreen.VideoView.IsVisible = false);
    }
}
