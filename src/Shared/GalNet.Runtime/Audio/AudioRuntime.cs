using GalNet.Core.Audio;
using GalNet.Presentation.Abstractions.Audio;

namespace GalNet.Runtime.Audio;

/// <summary>Game-scope logical audio state. It serializes queues and protects against late backend events.</summary>
public sealed class AudioRuntime : IDisposable
{
    private readonly object _gate = new();
    private readonly IAudioPlaybackHost? _host;
    private readonly Dictionary<string, TrackState> _tracks = new(StringComparer.Ordinal);
    private long _nextGeneration;
    private bool _disposed;

    public AudioRuntime(IAudioPlaybackHost? host = null)
    {
        _host = host;
        if (_host is not null) _host.EventReceived += OnBackendEvent;
    }

    public event Action<string>? Diagnostic;

    public void Play(IReadOnlyList<AudioTrackDefinition> definitions, AudioPlayRequest request)
    {
        lock (_gate)
        {
            var track = GetTrack(definitions, request.TrackId);
            if (track is null || string.IsNullOrWhiteSpace(request.AssetId)) return;

            if (request.Placement == AudioPlacement.ReplaceIfDifferent &&
                track.Active.Any(voice => string.Equals(voice.AssetId, request.AssetId, StringComparison.Ordinal)))
            {
                foreach (var voice in track.Active.Where(voice => string.Equals(voice.AssetId, request.AssetId, StringComparison.Ordinal)))
                {
                    voice.Gain = request.Gain;
                    Send(new(AudioBackendAction.SetGain, track.Definition.Id, voice.Id, voice.Generation, Gain: EffectiveGain(track, voice)));
                }
                return;
            }

            switch (request.Placement)
            {
                case AudioPlacement.Replace:
                case AudioPlacement.ReplaceAll:
                case AudioPlacement.ReplaceIfDifferent:
                    StopActive(track, 0);
                    if (request.Placement == AudioPlacement.ReplaceAll) track.Pending.Clear();
                    Start(track, request);
                    return;
                case AudioPlacement.Next:
                    if (track.Definition.MaxVoices != 1)
                    {
                        Report($"audio.next requires a single-voice track: '{track.Definition.Id}'.");
                        return;
                    }
                    StopActive(track, 0);
                    StartOrQueue(track, request, priority: true);
                    Pump(track);
                    return;
                default:
                    StartOrQueue(track, request, priority: false);
                    return;
            }
        }
    }

    public void Stop(IReadOnlyList<AudioTrackDefinition> definitions, AudioStopRequest request)
    {
        lock (_gate)
        {
            var track = GetTrack(definitions, request.TrackId);
            if (track is null) return;
            StopActive(track, request.FadeOutSeconds);
            if (request.ClearQueue) track.Pending.Clear();
        }
    }

    public void Pause(IReadOnlyList<AudioTrackDefinition> definitions, string trackId) => ApplyActive(definitions, trackId, AudioBackendAction.Pause);
    public void Resume(IReadOnlyList<AudioTrackDefinition> definitions, string trackId) => ApplyActive(definitions, trackId, AudioBackendAction.Resume);

    public void Next(IReadOnlyList<AudioTrackDefinition> definitions, string trackId)
    {
        lock (_gate)
        {
            var track = GetTrack(definitions, trackId);
            if (track is null) return;
            if (track.Definition.MaxVoices != 1)
            {
                Report($"audio.next requires a single-voice track: '{track.Definition.Id}'.");
                return;
            }
            StopActive(track, 0);
            Pump(track);
        }
    }

    public void Clear(IReadOnlyList<AudioTrackDefinition> definitions, string trackId)
    {
        lock (_gate)
        {
            var track = GetTrack(definitions, trackId);
            if (track is not null) track.Pending.Clear();
        }
    }

    public void ApplySEffect(IReadOnlyList<AudioTrackDefinition> definitions, AudioSEffectRequest request)
    {
        lock (_gate)
        {
            var track = GetTrack(definitions, request.TrackId);
            if (track is null || string.IsNullOrWhiteSpace(request.InstanceId) || string.IsNullOrWhiteSpace(request.SEffectId)) return;
            if (_host is null)
            {
                Report($"No audio playback host is available for '{request.TrackId}'.");
                return;
            }
            var diagnostics = _host.SEffects.Validate(request.SEffectId, SEffectScope.Track, request.Parameters);
            if (diagnostics.Count > 0)
            {
                foreach (var diagnostic in diagnostics) Report(diagnostic);
                return;
            }
            track.SEffects[request.InstanceId] = request;
            foreach (var voice in track.Active)
                Send(new(AudioBackendAction.ApplySEffect, track.Definition.Id, voice.Id, voice.Generation, SEffectId: request.SEffectId, SEffectInstanceId: request.InstanceId, SEffectParameters: request.Parameters));
        }
    }

    public void StopSEffect(IReadOnlyList<AudioTrackDefinition> definitions, string trackId, string instanceId)
    {
        lock (_gate)
        {
            var track = GetTrack(definitions, trackId);
            if (track is null || !track.SEffects.Remove(instanceId, out var effect)) return;
            foreach (var voice in track.Active)
                Send(new(AudioBackendAction.StopSEffect, track.Definition.Id, voice.Id, voice.Generation, SEffectId: effect.SEffectId, SEffectInstanceId: effect.InstanceId));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var track in _tracks.Values) StopActive(track, 0);
            _tracks.Clear();
            if (_host is not null) _host.EventReceived -= OnBackendEvent;
        }
    }

    private void ApplyActive(IReadOnlyList<AudioTrackDefinition> definitions, string trackId, AudioBackendAction action)
    {
        lock (_gate)
        {
            var track = GetTrack(definitions, trackId);
            if (track is null) return;
            foreach (var voice in track.Active) Send(new(action, track.Definition.Id, voice.Id, voice.Generation));
        }
    }

    private TrackState? GetTrack(IReadOnlyList<AudioTrackDefinition> definitions, string trackId)
    {
        if (_disposed) return null;
        var definition = definitions.FirstOrDefault(candidate => string.Equals(candidate.Id, trackId, StringComparison.Ordinal));
        if (definition is null)
        {
            Report($"Unknown audio track '{trackId}'.");
            return null;
        }
        if (!AudioBuses.IsValid(definition.Bus) || definition.MaxVoices <= 0 || definition.MaxQueuedItems < 0 || !float.IsFinite(definition.DefaultGain))
        {
            Report($"Invalid audio track definition '{trackId}'.");
            return null;
        }
        if (!_tracks.TryGetValue(trackId, out var state))
        {
            state = new TrackState(definition);
            _tracks.Add(trackId, state);
        }
        return state;
    }

    private void StartOrQueue(TrackState track, AudioPlayRequest request, bool priority)
    {
        if (track.Active.Count < track.Definition.MaxVoices)
        {
            Start(track, request);
            return;
        }
        if (track.Definition.MaxQueuedItems == 0)
        {
            if (track.Definition.OverflowPolicy == AudioOverflowPolicy.DropNewest)
            {
                Report($"Audio voice limit reached for '{track.Definition.Id}'; dropped '{request.AssetId}'.");
                return;
            }
            var oldest = track.Active.OrderBy(voice => voice.Generation).First();
            StopVoice(track, oldest, 0);
            Start(track, request);
            return;
        }
        if (track.Pending.Count >= track.Definition.MaxQueuedItems)
        {
            if (track.Definition.QueueOverflowPolicy == AudioQueueOverflowPolicy.DropNewest)
            {
                Report($"Audio queue limit reached for '{track.Definition.Id}'; dropped '{request.AssetId}'.");
                return;
            }
            track.Pending.RemoveAt(track.Pending.Count - 1);
        }
        if (priority) track.Pending.Insert(0, request); else track.Pending.Add(request);
    }

    private void Pump(TrackState track)
    {
        while (track.Active.Count < track.Definition.MaxVoices && track.Pending.Count > 0)
        {
            var request = track.Pending[0];
            track.Pending.RemoveAt(0);
            Start(track, request);
        }
    }

    private void Start(TrackState track, AudioPlayRequest request)
    {
        var generation = ++_nextGeneration;
        var voice = new VoiceState($"{track.Definition.Id}:{generation}", generation, request.AssetId, request.Gain);
        track.Active.Add(voice);
        Send(new(AudioBackendAction.Start, track.Definition.Id, voice.Id, voice.Generation, voice.AssetId, EffectiveGain(track, voice), request.FadeInSeconds));
        foreach (var effect in track.SEffects.Values)
            Send(new(AudioBackendAction.ApplySEffect, track.Definition.Id, voice.Id, voice.Generation, SEffectId: effect.SEffectId, SEffectInstanceId: effect.InstanceId, SEffectParameters: effect.Parameters));
    }

    private static float EffectiveGain(TrackState track, VoiceState voice) => Math.Clamp(track.Definition.DefaultGain * voice.Gain, 0f, 1f);

    private void StopActive(TrackState track, float fadeSeconds)
    {
        foreach (var voice in track.Active.ToArray()) StopVoice(track, voice, fadeSeconds);
    }

    private void StopVoice(TrackState track, VoiceState voice, float fadeSeconds)
    {
        track.Active.Remove(voice);
        Send(new(AudioBackendAction.Stop, track.Definition.Id, voice.Id, voice.Generation, FadeSeconds: Math.Max(0, fadeSeconds)));
    }

    private void OnBackendEvent(AudioBackendEvent backendEvent)
    {
        lock (_gate)
        {
            if (_disposed || !_tracks.TryGetValue(backendEvent.TrackId, out var track)) return;
            var voice = track.Active.FirstOrDefault(candidate => candidate.Id == backendEvent.VoiceId && candidate.Generation == backendEvent.Generation);
            if (voice is null) return;
            if (backendEvent.Kind is AudioBackendEventKind.Ended or AudioBackendEventKind.Failed)
            {
                track.Active.Remove(voice);
                if (backendEvent.Kind == AudioBackendEventKind.Failed) Report(backendEvent.Message ?? $"Audio backend failed for '{voice.AssetId}'.");
                Pump(track);
            }
        }
    }

    private void Send(AudioBackendRequest request)
    {
        if (_host is null)
        {
            Report($"No audio playback host is available for '{request.TrackId}'.");
            return;
        }
        if (request.Action is AudioBackendAction.ApplySEffect or AudioBackendAction.StopSEffect && !_host.Capabilities.SupportsTrackSEffects)
        {
            Report($"Audio backend does not support SEffect on '{request.TrackId}'.");
            return;
        }
        if (request.FadeSeconds > 0 && !_host.Capabilities.SupportsFade)
        {
            Report($"Audio backend does not support fades on '{request.TrackId}'; applying immediately.");
            request = request with { FadeSeconds = 0 };
        }
        _ = SendAsync(request);
    }

    private async Task SendAsync(AudioBackendRequest request)
    {
        try { await _host!.ApplyAsync(request).ConfigureAwait(false); }
        catch (Exception exception) { Report($"Audio backend request failed: {exception.Message}"); }
    }

    private void Report(string message) => Diagnostic?.Invoke(message);

    private sealed class TrackState(AudioTrackDefinition definition)
    {
        public AudioTrackDefinition Definition { get; } = definition;
        public List<VoiceState> Active { get; } = [];
        public List<AudioPlayRequest> Pending { get; } = [];
        public Dictionary<string, AudioSEffectRequest> SEffects { get; } = new(StringComparer.Ordinal);
    }

    private sealed class VoiceState(string id, long generation, string assetId, float gain)
    {
        public string Id { get; } = id;
        public long Generation { get; } = generation;
        public string AssetId { get; } = assetId;
        public float Gain { get; set; } = gain;
    }
}
