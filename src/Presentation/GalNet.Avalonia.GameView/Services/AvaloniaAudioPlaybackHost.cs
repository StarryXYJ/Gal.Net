using System.Security.Cryptography;
using System.Text;
using GalNet.Core.Assets;
using GalNet.Core.Audio;
using GalNet.Presentation.Abstractions.Audio;
using LibVLCSharp.Shared;
using LibVlcMedia = LibVLCSharp.Shared.Media;

namespace GalNet.Avalonia.GameView.Services;

/// <summary>LibVLC-backed audio host. Logical queue and track ownership remains in AudioRuntime.</summary>
public sealed class AvaloniaAudioPlaybackHost : IAudioPlaybackHost
{
    private readonly object _gate = new();
    private readonly IAssetManager _assets;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GalNet", "Audio", Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, Voice> _voices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly LibVLC? _libVlc;
    private bool _disposed;

    public AvaloniaAudioPlaybackHost(IAssetManager assets)
    {
        _assets = assets;
        Directory.CreateDirectory(_directory);
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            _libVlc = new LibVLC("--no-video-title-show");
            Capabilities = new(true, true, true);
        }
        catch (Exception exception)
        {
            InitializationError = exception.Message;
            Capabilities = AudioBackendCapabilities.None;
        }
    }

    public AudioBackendCapabilities Capabilities { get; }
    public string? InitializationError { get; }
    public event Action<AudioBackendEvent>? EventReceived;

    public async Task ApplyAsync(AudioBackendRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        switch (request.Action)
        {
            case AudioBackendAction.Start:
                await StartAsync(request, cancellationToken).ConfigureAwait(false);
                break;
            case AudioBackendAction.Stop:
                Stop(request, request.FadeSeconds);
                break;
            case AudioBackendAction.Pause:
                WithVoice(request, voice => voice.Player.Pause());
                break;
            case AudioBackendAction.Resume:
                WithVoice(request, voice => voice.Player.Play());
                break;
            case AudioBackendAction.SetGain:
                WithVoice(request, voice => { voice.BaseGain = request.Gain; SetVolume(voice); });
                break;
            case AudioBackendAction.ApplySEffect:
                ApplySEffect(request);
                break;
            case AudioBackendAction.StopSEffect:
                StopSEffect(request);
                break;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var voice in _voices.Values) voice.Dispose();
            _voices.Clear();
            _paths.Clear();
            _libVlc?.Dispose();
        }
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private async Task StartAsync(AudioBackendRequest request, CancellationToken cancellationToken)
    {
        if (_libVlc is null || string.IsNullOrWhiteSpace(request.AssetId))
        {
            Raise(new(AudioBackendEventKind.Failed, request.TrackId, request.VoiceId, request.Generation,
                InitializationError ?? "LibVLC audio is unavailable."));
            return;
        }

        string path;
        try { path = await MaterializeAsync(request.AssetId, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception)
        {
            Raise(new(AudioBackendEventKind.Failed, request.TrackId, request.VoiceId, request.Generation, exception.Message));
            return;
        }

        Voice voice;
        lock (_gate)
        {
            if (_disposed) return;
            RemoveVoice(request.VoiceId);
            var player = new MediaPlayer(_libVlc);
            voice = new Voice(request, player);
            player.EndReached += (_, _) => Ended(voice);
            player.EncounteredError += (_, _) => Failed(voice, "LibVLC could not play the audio asset.");
            _voices.Add(request.VoiceId, voice);
            voice.BaseGain = request.Gain;
            player.Volume = request.FadeSeconds > 0 ? 0 : ToVolume(request.Gain);
            using var media = new LibVlcMedia(_libVlc, path);
            player.Play(media);
        }
        Raise(new(AudioBackendEventKind.Started, request.TrackId, request.VoiceId, request.Generation));
        if (request.FadeSeconds > 0) _ = FadeAsync(voice, 0, request.Gain, request.FadeSeconds, stopWhenDone: false);
    }

    private async Task<string> MaterializeAsync(string assetId, CancellationToken cancellationToken)
    {
        lock (_gate)
            if (_paths.TryGetValue(assetId, out var existing)) return existing;
        var file = await _assets.GetFileAsync(assetId, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"Audio asset '{assetId}' was not found.");
        using var handle = await _assets.AcquireAsync<byte[]>(assetId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Audio asset '{assetId}' could not be decoded as bytes.");
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(assetId))).ToLowerInvariant();
        var path = Path.Combine(_directory, name + Path.GetExtension(file.Path));
        await File.WriteAllBytesAsync(path, handle.Value, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AvaloniaAudioPlaybackHost));
            _paths.TryAdd(assetId, path);
            return _paths[assetId];
        }
    }

    private void Stop(AudioBackendRequest request, float fadeSeconds) => WithVoice(request, voice =>
    {
        if (fadeSeconds <= 0)
        {
            voice.Player.Stop();
            RemoveVoice(voice.Request.VoiceId);
            return;
        }
        _ = FadeAsync(voice, CurrentGain(voice), 0, fadeSeconds, stopWhenDone: true);
    });

    private void ApplySEffect(AudioBackendRequest request) => WithVoice(request, voice =>
    {
        if (!string.Equals(request.SEffectId, "seffect.gain", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(request.SEffectInstanceId)) return;
        var gain = request.SEffectParameters is { } parameters && parameters.TryGetProperty("gain", out var value) && value.TryGetSingle(out var result)
            ? Math.Clamp(result, 0f, 1f) : 1f;
        voice.EffectGains[request.SEffectInstanceId] = gain;
        SetVolume(voice);
    });

    private void StopSEffect(AudioBackendRequest request) => WithVoice(request, voice =>
    {
        if (!string.IsNullOrWhiteSpace(request.SEffectInstanceId) && voice.EffectGains.Remove(request.SEffectInstanceId)) SetVolume(voice);
    });

    private async Task FadeAsync(Voice voice, float from, float to, float seconds, bool stopWhenDone)
    {
        const int steps = 20;
        try
        {
            for (var index = 1; index <= steps; index++)
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds / steps), voice.FadeCancellation.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    if (_disposed || !_voices.TryGetValue(voice.Request.VoiceId, out var current) || !ReferenceEquals(current, voice)) return;
                    voice.Player.Volume = ToVolume(from + (to - from) * index / steps);
                }
            }
            if (stopWhenDone)
            {
                lock (_gate)
                {
                    if (_voices.TryGetValue(voice.Request.VoiceId, out var current) && ReferenceEquals(current, voice))
                    {
                        voice.Player.Stop();
                        RemoveVoice(voice.Request.VoiceId);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void WithVoice(AudioBackendRequest request, Action<Voice> action)
    {
        lock (_gate)
            if (_voices.TryGetValue(request.VoiceId, out var voice) && voice.Request.Generation == request.Generation) action(voice);
    }

    private void Ended(Voice voice)
    {
        lock (_gate) RemoveVoice(voice.Request.VoiceId);
        Raise(new(AudioBackendEventKind.Ended, voice.Request.TrackId, voice.Request.VoiceId, voice.Request.Generation));
    }

    private void Failed(Voice voice, string message)
    {
        lock (_gate) RemoveVoice(voice.Request.VoiceId);
        Raise(new(AudioBackendEventKind.Failed, voice.Request.TrackId, voice.Request.VoiceId, voice.Request.Generation, message));
    }

    private void RemoveVoice(string voiceId)
    {
        if (_voices.Remove(voiceId, out var voice)) voice.Dispose();
    }

    private static float CurrentGain(Voice voice) => voice.BaseGain * voice.EffectGains.Values.Aggregate(1f, (gain, effect) => gain * effect);
    private static int ToVolume(float gain) => (int)Math.Round(Math.Clamp(gain, 0f, 1f) * 100);
    private static void SetVolume(Voice voice) => voice.Player.Volume = ToVolume(CurrentGain(voice));
    private void Raise(AudioBackendEvent @event) => EventReceived?.Invoke(@event);

    private sealed class Voice(AudioBackendRequest request, MediaPlayer player) : IDisposable
    {
        public AudioBackendRequest Request { get; } = request;
        public MediaPlayer Player { get; } = player;
        public float BaseGain { get; set; }
        public Dictionary<string, float> EffectGains { get; } = new(StringComparer.Ordinal);
        public CancellationTokenSource FadeCancellation { get; } = new();
        public void Dispose()
        {
            FadeCancellation.Cancel();
            FadeCancellation.Dispose();
            Player.Dispose();
        }
    }
}
