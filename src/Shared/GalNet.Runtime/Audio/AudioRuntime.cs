using GalNet.Core.Audio;
using GalNet.Core.Settings;
using GalNet.Presentation.Abstractions.Audio;

namespace GalNet.Runtime.Audio;

/// <summary>Game-scope logical audio state. It serializes queues and protects against late backend events.</summary>
public sealed class AudioRuntime : IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly IAudioPlaybackHost? _host;
    private readonly Dictionary<string, TrackState> _tracks = new(StringComparer.Ordinal);
    private readonly AudioBackendDispatcher _dispatcher;
    private long _nextGeneration;
    private bool _disposed;
    private GameSettings _settings = new();
    private bool _catchUp;

    public void BeginCatchUp()
    {
        lock (_gate)
        {
            if (_catchUp || _disposed) return;
            var projections = _tracks.Values.ToDictionary(track => track,
                track => track.Definition.Bus == AudioBuses.Bgm
                    ? (track.Active.Select(voice => voice.Request with { Gain = voice.Gain }).ToArray(), track.Pending.Select(item => item.Request).ToArray())
                    : (Array.Empty<AudioPlayRequest>(), Array.Empty<AudioPlayRequest>()));
            foreach (var track in _tracks.Values) { StopActive(track, 0); ClearPending(track); }
            _catchUp = true;
            foreach (var (track, (active, pending)) in projections)
            {
                foreach (var request in active) Start(track, new(request, new AudioPlaybackTicket(StopTicket)));
                foreach (var request in pending) track.Pending.Add(new(request, new AudioPlaybackTicket(StopTicket)));
            }
        }
    }

    public void EndCatchUp()
    {
        lock (_gate)
        {
            if (!_catchUp || _disposed) return;
            _catchUp = false;
            foreach (var track in _tracks.Values)
                foreach (var voice in track.Active.ToArray())
                {
                    Send(new(AudioBackendAction.Start, track.Definition.Id, voice.Id, voice.Generation, voice.AssetId,
                        EffectiveGain(track, voice)));
                    foreach (var effect in track.SEffects.Values)
                        Send(new(AudioBackendAction.ApplySEffect, track.Definition.Id, voice.Id, voice.Generation,
                            SEffectId: effect.SEffectId, SEffectInstanceId: effect.InstanceId, SEffectParameters: effect.Parameters));
                }
        }
    }

    public void ApplyPlayerSettings(GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            _settings = settings;
            foreach (var track in _tracks.Values)
                foreach (var voice in track.Active)
                    Send(new(AudioBackendAction.SetGain, track.Definition.Id, voice.Id, voice.Generation,
                        Gain: EffectiveGain(track, voice)));
        }
    }

    public AudioRuntime(IAudioPlaybackHost? host = null)
    {
        _host = host;
        _dispatcher = new AudioBackendDispatcher(host, _gate, Report, OnBackendEvent);
        if (_host is not null) _host.EventReceived += OnBackendEvent;
    }

    public event Action<string>? Diagnostic;

    public void Reset()
    {
        lock (_gate)
        {
            if (_disposed) return;
            foreach (var track in _tracks.Values) { StopActive(track, 0); ClearPending(track); }
            _tracks.Clear();
            _catchUp = false;
        }
    }

    public AudioPlaybackTicket Play(IReadOnlyList<AudioTrackDefinition> definitions, AudioPlayRequest request)
    {
        lock (_gate)
        {
            var ticket = new AudioPlaybackTicket(StopTicket);
            var item = new QueuedPlay(request, ticket);
            var track = GetTrack(definitions, request.TrackId);
            if (track is null || string.IsNullOrWhiteSpace(request.AssetId)) { ticket.Complete(); return ticket; }
            if (_catchUp && track.Definition.Bus != AudioBuses.Bgm) { ticket.Complete(); return ticket; }
            if (_host is null)
            {
                Report($"No audio playback host is available for '{request.TrackId}'.");
                ticket.Complete();
                return ticket;
            }

            if (request.Placement == AudioPlacement.ReplaceIfDifferent &&
                track.Active.Any(voice => string.Equals(voice.AssetId, request.AssetId, StringComparison.Ordinal)))
            {
                foreach (var voice in track.Active.Where(voice => string.Equals(voice.AssetId, request.AssetId, StringComparison.Ordinal)))
                {
                    voice.Gain = request.Gain;
                    Send(new(AudioBackendAction.SetGain, track.Definition.Id, voice.Id, voice.Generation, Gain: EffectiveGain(track, voice)));
                }
                track.Active.First(voice => voice.AssetId == request.AssetId).Tickets.Add(ticket);
                return ticket;
            }

            switch (request.Placement)
            {
                case AudioPlacement.Replace:
                case AudioPlacement.ReplaceAll:
                case AudioPlacement.ReplaceIfDifferent:
                    StopActive(track, 0);
                    if (request.Placement == AudioPlacement.ReplaceAll) ClearPending(track);
                    Start(track, item);
                    return ticket;
                case AudioPlacement.Next:
                    if (track.Definition.MaxVoices != 1)
                    {
                        Report($"audio.next requires a single-voice track: '{track.Definition.Id}'.");
                        ticket.Complete();
                        return ticket;
                    }
                    StopActive(track, 0);
                    StartOrQueue(track, item, priority: true);
                    Pump(track);
                    return ticket;
                default:
                    StartOrQueue(track, item, priority: false);
                    return ticket;
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
            if (request.ClearQueue) ClearPending(track);
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
            if (track is not null) ClearPending(track);
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
            foreach (var track in _tracks.Values) { StopActive(track, 0); ClearPending(track); }
            _tracks.Clear();
            if (_host is not null) _host.EventReceived -= OnBackendEvent;
        }
    }

    /// <summary>Stops voices and waits for all backend commands before the host is released.</summary>
    public async ValueTask DisposeAsync()
    {
        Dispose();
        await _dispatcher.DrainAsync().ConfigureAwait(false);
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

    private void StartOrQueue(TrackState track, QueuedPlay item, bool priority)
    {
        var request = item.Request;
        if (track.Active.Count < track.Definition.MaxVoices)
        {
            Start(track, item);
            return;
        }
        if (track.Definition.MaxQueuedItems == 0)
        {
            if (track.Definition.OverflowPolicy == AudioOverflowPolicy.DropNewest)
            {
                Report($"Audio voice limit reached for '{track.Definition.Id}'; dropped '{request.AssetId}'.");
                item.Ticket.Complete();
                return;
            }
            var oldest = track.Active.OrderBy(voice => voice.Generation).First();
            StopVoice(track, oldest, 0);
            Start(track, item);
            return;
        }
        if (track.Pending.Count >= track.Definition.MaxQueuedItems)
        {
            if (track.Definition.QueueOverflowPolicy == AudioQueueOverflowPolicy.DropNewest)
            {
                Report($"Audio queue limit reached for '{track.Definition.Id}'; dropped '{request.AssetId}'.");
                item.Ticket.Complete();
                return;
            }
            track.Pending[0].Ticket.Complete();
            track.Pending.RemoveAt(0);
        }
        if (priority) track.Pending.Insert(0, item); else track.Pending.Add(item);
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

    private void Start(TrackState track, QueuedPlay item)
    {
        var request = item.Request;
        var generation = ++_nextGeneration;
        var voice = new VoiceState($"{track.Definition.Id}:{generation}", generation, request);
        voice.Tickets.Add(item.Ticket);
        track.Active.Add(voice);
        Send(new(AudioBackendAction.Start, track.Definition.Id, voice.Id, voice.Generation, voice.AssetId, EffectiveGain(track, voice), request.FadeInSeconds));
        foreach (var effect in track.SEffects.Values)
            Send(new(AudioBackendAction.ApplySEffect, track.Definition.Id, voice.Id, voice.Generation, SEffectId: effect.SEffectId, SEffectInstanceId: effect.InstanceId, SEffectParameters: effect.Parameters));
    }

    private float EffectiveGain(TrackState track, VoiceState voice)
    {
        var (volume, muted) = track.Definition.Bus switch
        {
            AudioBuses.Bgm => (_settings.BgmVolume, _settings.BgmMuted),
            AudioBuses.Vocal => (_settings.VoiceVolume, _settings.VoiceMuted),
            _ => (_settings.SfxVolume, _settings.SfxMuted)
        };
        if (_settings.MasterMuted || muted) return 0;
        var gain = track.Definition.DefaultGain * voice.Gain * volume * _settings.MasterVolume;
        return float.IsFinite(gain) ? Math.Clamp(gain, 0f, 1f) : 0;
    }

    private void StopActive(TrackState track, float fadeSeconds)
    {
        foreach (var voice in track.Active.ToArray()) StopVoice(track, voice, fadeSeconds);
    }

    private void StopVoice(TrackState track, VoiceState voice, float fadeSeconds)
    {
        track.Active.Remove(voice);
        foreach (var ticket in voice.Tickets) ticket.Complete();
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
                foreach (var ticket in voice.Tickets) ticket.Complete();
                _dispatcher.CompleteVoice(voice.Id);
                if (backendEvent.Kind == AudioBackendEventKind.Failed) Report(backendEvent.Message ?? $"Audio backend failed for '{voice.AssetId}'.");
                Pump(track);
            }
        }
    }

    private void Send(AudioBackendRequest request)
    {
        if (!_catchUp) _dispatcher.Enqueue(request);
    }

    private void Report(string message) => Diagnostic?.Invoke(message);

    private sealed class TrackState(AudioTrackDefinition definition)
    {
        public AudioTrackDefinition Definition { get; } = definition;
        public List<VoiceState> Active { get; } = [];
        public List<QueuedPlay> Pending { get; } = [];
        public Dictionary<string, AudioSEffectRequest> SEffects { get; } = new(StringComparer.Ordinal);
    }

    private sealed class VoiceState(string id, long generation, AudioPlayRequest request)
    {
        public string Id { get; } = id;
        public long Generation { get; } = generation;
        public AudioPlayRequest Request { get; } = request;
        public string AssetId { get; } = request.AssetId;
        public float Gain { get; set; } = request.Gain;
        public List<AudioPlaybackTicket> Tickets { get; } = [];
    }

    private sealed record QueuedPlay(AudioPlayRequest Request, AudioPlaybackTicket Ticket);

    private static void ClearPending(TrackState track)
    {
        foreach (var item in track.Pending) item.Ticket.Complete();
        track.Pending.Clear();
    }

    private void StopTicket(AudioPlaybackTicket ticket)
    {
        lock (_gate)
        {
            foreach (var track in _tracks.Values)
            {
                track.Pending.RemoveAll(item => ReferenceEquals(item.Ticket, ticket));
                foreach (var voice in track.Active.Where(voice => voice.Tickets.Contains(ticket)).ToArray())
                    StopVoice(track, voice, 0);
                Pump(track);
            }
            ticket.Complete();
        }
    }
}
