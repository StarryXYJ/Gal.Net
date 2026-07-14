using System;
using System.Collections.Generic;
using LibVLCSharp.Shared;

namespace GalNet.Control.View;

/// <summary>
/// 音频子系统控制器 —— 封装 LibVLC MediaPlayer，将音频业务逻辑从 DefaultGameView 隔离开。
/// </summary>
public sealed class AudioController : IDisposable
{
    private readonly LibVLC? _libVlc;
    private readonly bool _vlcInitialized;
    private readonly Dictionary<string, MediaPlayer> _audioPlayers = new(StringComparer.OrdinalIgnoreCase);

    public AudioController(LibVLC? libVlc, bool vlcInitialized)
    {
        _libVlc = libVlc;
        _vlcInitialized = vlcInitialized;
    }

    public void Play(string channel, string assetId, float volume)
    {
        if (!_vlcInitialized || _libVlc == null) return;
        var player = GetPlayer(channel);
        player.Stop();
        using var media = new Media(_libVlc, assetId);
        player.Play(media);
        player.Volume = (int)(volume * 100);
    }

    public void Stop(string channel)
    {
        if (!_vlcInitialized) return;
        GetPlayer(channel).Stop();
    }

    public void StopAll()
    {
        if (!_vlcInitialized) return;
        foreach (var player in _audioPlayers.Values)
            player.Stop();
    }

    public void Pause(string channel)
    {
        if (!_vlcInitialized) return;
        GetPlayer(channel).Pause();
    }

    public void Resume(string channel)
    {
        if (!_vlcInitialized) return;
        GetPlayer(channel).Play();
    }

    public void Enqueue(string channel, string assetId)
    {
        if (!_vlcInitialized || _libVlc == null) return;
        GetPlayer(channel).Play(new Media(_libVlc, assetId));
    }

    private MediaPlayer GetPlayer(string channel)
    {
        if (!_audioPlayers.TryGetValue(channel, out var player))
        {
            player = new MediaPlayer(_libVlc);
            _audioPlayers[channel] = player;
        }
        return player;
    }

    public void Dispose()
    {
        foreach (var player in _audioPlayers.Values)
            player.Dispose();
        _audioPlayers.Clear();
    }
}
