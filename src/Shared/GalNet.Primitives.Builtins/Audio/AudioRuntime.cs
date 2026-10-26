using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Runtime;

namespace GalNet.Primitives.Builtins.Audio;

/// <summary>Projects canonical audio state to backend voices. Only tickets and backend identities live here.</summary>
public sealed class AudioRuntime : IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly IAudioPlaybackHost? _host;
    private readonly ISEffectCatalog _effects;
    private readonly AudioBackendDispatcher _dispatcher;
    private readonly Dictionary<string, TrackBinding> _tracks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, VoiceBinding> _voices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<AudioPlaybackTicket>> _tickets = new(StringComparer.Ordinal);
    private GameState _state;
    private long _nextGeneration;
    private AudioMixOptions _mix = new();
    private bool _disposed;
    private bool _catchUp;
    public event Action<string>? Diagnostic;

    public AudioRuntime(IAudioPlaybackHost? host = null, ISEffectCatalog? effects = null)
    {
        _host = host;
        _effects = effects ?? host?.SEffects ?? SEffectCatalog.Builtins;
        var builder = new StateCatalogBuilder();
        builder.Register<AudioTrackState>("builtin.audio.tracks", value => value.Id, ValidateTrack);
        builder.Register<AudioSEffectState>("builtin.audio.seffects", value => AudioSEffectState.Key(value.TrackId, value.InstanceId), ValidateEffect);
        _state = new GameState(builder.Build());
        _dispatcher = new(host, _gate, Report, OnBackendEvent);
        if (host is not null) host.EventReceived += OnBackendEvent;
    }

    public void Bind(GameState state)
    {
        lock (_gate)
        {
            if (ReferenceEquals(state, _state)) return;
            StopBindings();
            _state = state;
            _tracks.Clear();
        }
    }
    public async Task SuspendAsync(GameState expected)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_state, expected)) return;
            StopBindings();
        }
        await _dispatcher.DrainAsync().ConfigureAwait(false);
    }
    public void Reset(GameState? expected = null)
    {
        lock (_gate) _state.Update(() =>
        {
            if (_disposed || expected is not null && !ReferenceEquals(expected, _state)) return;
            StopBindings();
            _state.Table<AudioTrackState>().Clear();
            _state.Table<AudioSEffectState>().Clear();
            _tracks.Clear();
            _catchUp = false;
        });
    }
    public void Validate(GameState state, AudioConfiguration configuration)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var track in state.Table<AudioTrackState>().Values)
        {
            ValidateTrack(track);
            if (track.Active.Concat(track.Pending).Any(item => !identities.Add(item.Id)))
                throw new InvalidDataException("Saved audio item identities must be unique across tracks.");
            if (!configuration.TryGetTrack(track.Id, out var definition))
                throw new InvalidDataException($"Unknown saved audio track '{track.Id}'.");
            if (track.Active.Count > definition.MaxVoices || track.Pending.Count > definition.MaxQueuedItems)
                throw new InvalidDataException($"Saved audio track '{track.Id}' exceeds its configured limits.");
        }
        foreach (var effect in state.Table<AudioSEffectState>().Values)
        {
            ValidateEffect(effect);
            if (!state.Table<AudioTrackState>().TryGet(effect.TrackId, out _)) throw new InvalidDataException("An audio effect references a missing track.");
        }
    }
    public static void ValidateTrack(AudioTrackState track)
    {
        if (string.IsNullOrWhiteSpace(track.Id) || track.Active is null || track.Pending is null)
            throw new InvalidDataException("An audio track requires an ID and playback collections.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in track.Active.Concat(track.Pending))
            if (item is null || item.Request is null || string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id) || item.Request.TrackId != track.Id ||
                string.IsNullOrWhiteSpace(item.Request.AssetId) || !float.IsFinite(item.Request.Gain) || item.Request.Gain < 0 ||
                !float.IsFinite(item.Request.FadeInSeconds) || item.Request.FadeInSeconds < 0)
                throw new InvalidDataException($"Invalid playback item in track '{track.Id}'.");
    }
    private void ValidateEffect(AudioSEffectState effect)
    {
        if (string.IsNullOrWhiteSpace(effect.TrackId) || string.IsNullOrWhiteSpace(effect.InstanceId)) throw new InvalidDataException("An audio effect requires track and instance IDs.");
        var diagnostics = _effects.Validate(effect.SEffectId, SEffectScope.Track, effect.Parameters);
        if (diagnostics.Count > 0) throw new InvalidDataException(string.Join(" ", diagnostics));
    }
    public async Task RestoreAsync(GameState state, AudioConfiguration configuration, CancellationToken token)
    {
        Validate(state, configuration);
        Bind(state);
        lock (_gate) StopBindings();
        await _dispatcher.DrainAsync().WaitAsync(token).ConfigureAwait(false);
        lock (_gate) _state.Update(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_state, state)) throw new OperationCanceledException("The audio run was replaced.", token);
            foreach (var saved in _state.Table<AudioTrackState>().Values)
            {
                var track = GetTrack(configuration, saved.Id)!;
                foreach (var item in saved.Active.ToArray()) Project(track, item, fade: 0);
            }
        });
    }
    public void BeginCatchUp()
    {
        lock (_gate) _state.Update(() =>
        {
            if (_catchUp || _disposed) return;
            StopBindings();
            _catchUp = true;
            // CatchUp projection is a declared track policy, independent of group names.
            foreach (var track in _tracks.Values.Where(track => !track.Definition.PreserveDuringCatchUp))
            { track.State.Active.Clear(); track.State.Pending.Clear(); }
        });
    }
    public void EndCatchUp()
    {
        lock (_gate) _state.Update(() =>
        {
            if (!_catchUp || _disposed) return;
            _catchUp = false;
            foreach (var track in _tracks.Values)
                foreach (var item in track.State.Active.ToArray()) Project(track, item, 0);
        });
    }
    public void ApplyMix(AudioMixOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = options.Snapshot();
        lock (_gate) _state.Update(() =>
        {
            _mix = snapshot;
            foreach (var track in _tracks.Values)
                foreach (var item in track.State.Active.ToArray()) SendItem(track, item, AudioBackendAction.SetGain);
        });
    }
    public AudioPlaybackTicket Play(AudioConfiguration configuration, AudioPlayRequest request)
    {
        lock (_gate) return _state.Update(() =>
        {
            var ticket = new AudioPlaybackTicket(StopTicket);
            var track = GetTrack(configuration, request.TrackId);
            if (track is null || string.IsNullOrWhiteSpace(request.AssetId) || _catchUp && !track.Definition.PreserveDuringCatchUp)
            { ticket.Complete(); return ticket; }
            if (request.Placement == AudioPlacement.ReplaceIfDifferent)
            {
                var same = track.State.Active.Where(item => item.Request.AssetId == request.AssetId).ToArray();
                if (same.Length > 0)
                {
                    foreach (var item in same) { item.Request = item.Request with { Gain = request.Gain }; SendItem(track, item, AudioBackendAction.SetGain); }
                    if (_host is not null) Tickets(same[0].Id).Add(ticket);
                    if (_host is null) ticket.Complete();
                    return ticket;
                }
            }
            var value = new AudioItemState { Request = request };
            if (_host is not null) Tickets(value.Id).Add(ticket);
            switch (request.Placement)
            {
                case AudioPlacement.Replace:
                case AudioPlacement.ReplaceAll:
                case AudioPlacement.ReplaceIfDifferent:
                    StopActive(track, 0);
                    if (request.Placement == AudioPlacement.ReplaceAll) ClearPending(track);
                    Start(track, value); break;
                case AudioPlacement.Next:
                    if (track.Definition.MaxVoices != 1) { Report("audio.next requires a single-voice track."); Complete(value); break; }
                    StopActive(track, 0); QueueOrStart(track, value, true); Pump(track); break;
                default: QueueOrStart(track, value, false); break;
            }
            if (_host is null) ticket.Complete();
            return ticket;
        });
    }
    public void Stop(AudioConfiguration configuration, AudioStopRequest request)
    {
        lock (_gate) _state.Update(() =>
        {
            var track = GetTrack(configuration, request.TrackId); if (track is null) return;
            StopActive(track, request.FadeOutSeconds); if (request.ClearQueue) ClearPending(track);
        });
    }
    public void Pause(AudioConfiguration configuration, string trackId) => SetPaused(configuration, trackId, true);
    public void Resume(AudioConfiguration configuration, string trackId) => SetPaused(configuration, trackId, false);
    private void SetPaused(AudioConfiguration configuration, string trackId, bool paused)
    {
        lock (_gate) _state.Update(() =>
        {
            var track = GetTrack(configuration, trackId); if (track is null) return;
            track.State.Paused = paused;
            foreach (var item in track.State.Active.ToArray())
                if (!paused && !_voices.ContainsKey(item.Id)) Project(track, item, 0);
                else SendItem(track, item, paused ? AudioBackendAction.Pause : AudioBackendAction.Resume);
            if (!paused) Pump(track);
        });
    }
    public void Next(AudioConfiguration configuration, string trackId)
    {
        lock (_gate) _state.Update(() =>
        {
            var track = GetTrack(configuration, trackId); if (track is null) return;
            if (track.Definition.MaxVoices != 1) { Report("audio.next requires a single-voice track."); return; }
            StopActive(track, 0); Pump(track);
        });
    }
    public void Clear(AudioConfiguration configuration, string trackId)
    { lock (_gate) _state.Update(() => { var track = GetTrack(configuration, trackId); if (track is not null) ClearPending(track); }); }
    public void ApplySEffect(AudioConfiguration configuration, AudioSEffectRequest request)
    {
        lock (_gate) _state.Update(() =>
        {
            var track = GetTrack(configuration, request.TrackId); if (track is null) return;
            var effect = new AudioSEffectState(request.TrackId, request.InstanceId, request.SEffectId, request.Parameters.Clone());
            try { ValidateEffect(effect); } catch (InvalidDataException error) { Report(error.Message); return; }
            _state.Table<AudioSEffectState>().Set(effect);
            foreach (var item in track.State.Active.ToArray()) SendEffect(track, item, effect, AudioBackendAction.ApplySEffect);
        });
    }
    public void StopSEffect(AudioConfiguration configuration, string trackId, string instanceId)
    {
        lock (_gate) _state.Update(() =>
        {
            var track = GetTrack(configuration, trackId); if (track is null) return;
            var table = _state.Table<AudioSEffectState>();
            var key = AudioSEffectState.Key(trackId, instanceId);
            if (!table.TryGet(key, out var effect)) return;
            table.Remove(key);
            foreach (var item in track.State.Active.ToArray()) SendEffect(track, item, effect, AudioBackendAction.StopSEffect);
        });
    }
    private TrackBinding? GetTrack(AudioConfiguration configuration, string id)
    {
        if (_disposed) return null;
        if (_tracks.TryGetValue(id, out var cached)) return cached;
        var definition = configuration.TryGetTrack(id, out var configured) ? configured : null;
        if (definition is null)
        { Report($"Unknown or invalid audio track '{id}'."); return null; }
        var table = _state.Table<AudioTrackState>();
        if (!table.TryGet(id, out var data)) { data = new AudioTrackState { Id = id }; table.Add(data); }
        var track = new TrackBinding(definition, data); _tracks.Add(id, track); return track;
    }
    private void QueueOrStart(TrackBinding track, AudioItemState item, bool priority)
    {
        if (track.State.Active.Count < track.Definition.MaxVoices) { Start(track, item); return; }
        if (track.Definition.MaxQueuedItems == 0)
        {
            if (track.Definition.OverflowPolicy == AudioOverflowPolicy.DropNewest) { Report("Audio voice limit reached."); Complete(item); return; }
            StopVoice(track, track.State.Active[0], 0); Start(track, item); return;
        }
        if (track.State.Pending.Count >= track.Definition.MaxQueuedItems)
        {
            if (track.Definition.QueueOverflowPolicy == AudioQueueOverflowPolicy.DropNewest) { Report("Audio queue limit reached."); Complete(item); return; }
            Complete(track.State.Pending[0]); track.State.Pending.RemoveAt(0);
        }
        if (priority) track.State.Pending.Insert(0, item); else track.State.Pending.Add(item);
    }
    private void Start(TrackBinding track, AudioItemState item) { track.State.Active.Add(item); Project(track, item, item.Request.FadeInSeconds); }
    private void Pump(TrackBinding track)
    {
        while (!track.State.Paused && track.State.Active.Count < track.Definition.MaxVoices && track.State.Pending.Count > 0)
        { var item = track.State.Pending[0]; track.State.Pending.RemoveAt(0); Start(track, item); }
    }
    private void Project(TrackBinding track, AudioItemState item, float fade)
    {
        if (_catchUp || track.State.Paused || _host is null) return;
        var generation = ++_nextGeneration;
        var binding = new VoiceBinding(track.State.Id, $"{track.Definition.Id}:{generation}", generation);
        _voices[item.Id] = binding;
        _dispatcher.Enqueue(new(AudioBackendAction.Start, track.Definition.Id, binding.Id, generation, item.Request.AssetId, Gain(track, item), fade));
        foreach (var effect in _state.Table<AudioSEffectState>().Values.Where(value => value.TrackId == track.State.Id))
            SendEffect(track, item, effect, AudioBackendAction.ApplySEffect);
    }
    private void SendItem(TrackBinding track, AudioItemState item, AudioBackendAction action, float fade = 0)
    {
        if (!_catchUp && _voices.TryGetValue(item.Id, out var binding))
            _dispatcher.Enqueue(new(action, track.State.Id, binding.Id, binding.Generation, Gain: Gain(track, item), FadeSeconds: fade));
    }
    private void SendEffect(TrackBinding track, AudioItemState item, AudioSEffectState effect, AudioBackendAction action)
    {
        if (!_catchUp && _voices.TryGetValue(item.Id, out var binding))
            _dispatcher.Enqueue(new(action, track.State.Id, binding.Id, binding.Generation, SEffectId: effect.SEffectId,
                SEffectInstanceId: effect.InstanceId, SEffectParameters: effect.Parameters));
    }
    private float Gain(TrackBinding track, AudioItemState item)
    {
        var group = track.Definition.GroupId is { } groupId ? _mix.Groups.GetValueOrDefault(groupId) : null;
        _mix.Tracks.TryGetValue(track.Definition.Id, out var trackMix);
        if (_mix.MasterMuted || group?.Muted == true || trackMix?.Muted == true) return 0;
        var gain = (double)track.Definition.DefaultGain * item.Request.Gain * (group?.Gain ?? 1f) * (trackMix?.Gain ?? 1f) * _mix.MasterGain;
        return (float)Math.Clamp(gain, 0, 1);
    }
    private List<AudioPlaybackTicket> Tickets(string id)
    { if (!_tickets.TryGetValue(id, out var tickets)) _tickets[id] = tickets = []; return tickets; }
    private void Complete(AudioItemState item)
    { if (_tickets.Remove(item.Id, out var tickets)) foreach (var ticket in tickets) ticket.Complete(); }
    private void StopVoice(TrackBinding track, AudioItemState item, float fade)
    {
        track.State.Active.Remove(item); SendItem(track, item, AudioBackendAction.Stop, Math.Max(0, fade));
        _voices.Remove(item.Id); Complete(item);
    }
    private void StopActive(TrackBinding track, float fade) { foreach (var item in track.State.Active.ToArray()) StopVoice(track, item, fade); }
    private void ClearPending(TrackBinding track) { foreach (var item in track.State.Pending) Complete(item); track.State.Pending.Clear(); }
    private void StopBindings()
    {
        var bindings = _voices.Values.ToArray();
        _voices.Clear();
        var pendingTickets = _tickets.Values.SelectMany(tickets => tickets).ToArray();
        _tickets.Clear();
        // Some hosts report an end synchronously from Stop. Invalidate identities before calling them.
        foreach (var binding in bindings)
            _dispatcher.Enqueue(new(AudioBackendAction.Stop, binding.TrackId, binding.Id, binding.Generation));
        foreach (var ticket in pendingTickets) ticket.Complete();
    }
    private void OnBackendEvent(AudioBackendEvent value)
    {
        lock (_gate) _state.Update(() =>
        {
            if (_disposed || value.Kind is not (AudioBackendEventKind.Ended or AudioBackendEventKind.Failed) || !_tracks.TryGetValue(value.TrackId, out var track)) return;
            var item = track.State.Active.FirstOrDefault(item => _voices.TryGetValue(item.Id, out var voice) && voice.Id == value.VoiceId && voice.Generation == value.Generation);
            if (item is null) return;
            track.State.Active.Remove(item); _voices.Remove(item.Id); _dispatcher.CompleteVoice(value.VoiceId);
            if (value.Kind == AudioBackendEventKind.Failed) Report(value.Message ?? $"Audio backend failed for '{item.Request.AssetId}'.");
            Pump(track);
            Complete(item);
        });
    }
    private void StopTicket(AudioPlaybackTicket ticket)
    {
        lock (_gate) _state.Update(() =>
        {
            foreach (var track in _tracks.Values)
            {
                foreach (var item in track.State.Pending.Where(item => Tickets(item.Id).Contains(ticket)).ToArray())
                { track.State.Pending.Remove(item); Complete(item); }
                foreach (var item in track.State.Active.Where(item => Tickets(item.Id).Contains(ticket)).ToArray()) StopVoice(track, item, 0);
                Pump(track);
            }
            ticket.Complete();
        });
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return; _disposed = true;
            StopBindings(); if (_host is not null) _host.EventReceived -= OnBackendEvent;
        }
    }
    public async ValueTask DisposeAsync() { Dispose(); await _dispatcher.DrainAsync().ConfigureAwait(false); }
    private void Report(string message)
    {
        foreach (var handler in Diagnostic?.GetInvocationList() ?? [])
            try { ((Action<string>)handler)(message); }
            catch (Exception) { /* Diagnostic observers cannot interrupt backend cleanup. */ }
    }
    private sealed record TrackBinding(AudioTrackDefinition Definition, AudioTrackState State);
    private sealed record VoiceBinding(string TrackId, string Id, long Generation);
}
