using LibVLCSharp.Shared;
using LibVlcMedia = LibVLCSharp.Shared.Media;

namespace GalNet.Presentation.Defaults.Media;

/// <summary>Framework-neutral LibVLC audio controller for UI hosts that choose this backend.</summary>
public sealed class LibVlcAudioController : IDisposable
{
    private readonly LibVLC? _libVlc;
    private readonly Dictionary<string, MediaPlayer> _players = new(StringComparer.OrdinalIgnoreCase);

    public LibVlcAudioController(LibVLC? libVlc) => _libVlc = libVlc;

    public void Play(string channel, string assetId, float volume)
    {
        if (_libVlc is null) return;
        var player = GetPlayer(channel);
        player.Stop();
        using var media = new LibVlcMedia(_libVlc, assetId);
        player.Play(media);
        player.Volume = (int)(Math.Clamp(volume, 0, 1) * 100);
    }

    public void Stop(string channel) => TryGetPlayer(channel)?.Stop();
    public void Pause(string channel) => TryGetPlayer(channel)?.Pause();
    public void Resume(string channel) => TryGetPlayer(channel)?.Play();

    public void Enqueue(string channel, string assetId)
    {
        if (_libVlc is null) return;
        GetPlayer(channel).Play(new LibVlcMedia(_libVlc, assetId));
    }

    public void StopAll()
    {
        foreach (var player in _players.Values) player.Stop();
    }

    public void Dispose()
    {
        foreach (var player in _players.Values) player.Dispose();
        _players.Clear();
    }

    private MediaPlayer GetPlayer(string channel)
    {
        if (_libVlc is null) throw new InvalidOperationException("LibVLC is unavailable.");
        if (!_players.TryGetValue(channel, out var player))
        {
            player = new MediaPlayer(_libVlc);
            _players[channel] = player;
        }
        return player;
    }

    private MediaPlayer? TryGetPlayer(string channel) => _players.GetValueOrDefault(channel);
}
