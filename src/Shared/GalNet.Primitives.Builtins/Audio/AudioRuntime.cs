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
    private readonly Dictionary<string, RetiringVoice> _retiring = new(StringComparer.Ordinal);
    private GameState _state;
    private long _nextGeneration;
    private AudioMixOptions _mix = new();
    private bool _disposed;
    private bool _catchUp;
    private AudioConfiguration? _configuration;
    private AudioConfiguration? _declarations;
    public event Action<string>? Diagnostic;

    public AudioRuntime(IAudioPlaybackHost? host = null, ISEffectCatalog? effects = null)
    {
        _host = host;
        _effects = effects ?? host?.SEffects ?? SEffectCatalog.Builtins;
        var builder = new StateCatalogBuilder();
        builder.Register<AudioTrackState>("builtin.audio.tracks", value => value.Id, ValidateTrack);
        builder.Register<AudioSEffectState>("builtin.audio.seffects", value => value.Key(), ValidateEffect);
        builder.Register<AudioChannelState>("builtin.audio.channels", value => value.Key(), ValidateChannel);
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
            ResetMixer();
        }
    }
    public async Task SuspendAsync(GameState expected)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_state, expected)) return;
            StopBindings();
            ResetMixer();
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
            _state.Table<AudioChannelState>().Clear();
            ResetMixer();
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
            if (!configuration.HasTarget(effect.Scope, effect.TargetId)) throw new InvalidDataException("An audio effect references a missing target.");
        }
        if (state.Table<AudioSEffectState>().Values.GroupBy(value => (value.Scope, value.TargetId)).Any(group => group.Count() > 64))
            throw new InvalidDataException("An audio target supports at most 64 active effects.");
        foreach (var channel in state.Table<AudioChannelState>().Values)
        {
            ValidateChannel(channel);
            if (!configuration.HasTarget(channel.Scope, channel.TargetId)) throw new InvalidDataException("An audio level references a missing target.");
        }
    }
    public static void ValidateChannel(AudioChannelState channel)
    {
        if (string.IsNullOrWhiteSpace(channel.TargetId) || channel.Scope is not (SEffectScope.Track or SEffectScope.Bus) || !float.IsFinite(channel.Gain) || channel.Gain < 0 || channel.Gain > 1)
            throw new InvalidDataException("Invalid audio channel level.");
    }
    public static void ValidateTrack(AudioTrackState track)
    {
        if (string.IsNullOrWhiteSpace(track.Id) || track.Active is null || track.Pending is null)
            throw new InvalidDataException("An audio track requires an ID and playback collections.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in track.Active.Concat(track.Pending))
            if (item is null || item.Request is null || string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id) || item.Request.TrackId != track.Id ||
                string.IsNullOrWhiteSpace(item.Request.AssetId) || !float.IsFinite(item.Request.Gain) || item.Request.Gain < 0 ||
                !float.IsFinite(item.Request.FadeInSeconds) || item.Request.FadeInSeconds < 0 ||
                !float.IsFinite(item.Request.FadeOutSeconds) || item.Request.FadeOutSeconds < 0 ||
                !Enum.IsDefined(item.Request.Placement) || !Enum.IsDefined(item.Request.Completion) || item.Request.LoopCount < 0 ||
                item.CompletedIterations < 0 || item.Request.LoopCount > 0 && item.CompletedIterations >= item.Request.LoopCount ||
                item.Request.LoopCount == 0 && item.CompletedIterations != 0)
                throw new InvalidDataException($"Invalid playback item in track '{track.Id}'.");
    }
    private void ValidateEffect(AudioSEffectState effect)
    {
        if (string.IsNullOrWhiteSpace(effect.TargetId) || string.IsNullOrWhiteSpace(effect.InstanceId)) throw new InvalidDataException("An audio effect requires track and instance IDs.");
        var diagnostics = _effects.Validate(effect.SEffectId, effect.Scope, effect.Parameters);
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
            ResetMixer();
            Configure(configuration);
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
            ResetMixer();
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
            if (_declarations is { } configuration) { ResetMixer(); Configure(configuration); }
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
            if (_host?.Capabilities.SupportsMixer == true)
            {
                _dispatcher.Enqueue(new(AudioBackendAction.SetMix, "", "", 0, Mix: snapshot));
                return;
            }
            foreach (var track in _tracks.Values)
                foreach (var item in track.State.Active.ToArray()) SendItem(track, item, AudioBackendAction.SetGain);
            foreach (var retiring in _retiring.Values.ToArray())
                _dispatcher.Enqueue(new(AudioBackendAction.SetGain, retiring.Binding.TrackId, retiring.Binding.Id,
                    retiring.Binding.Generation, Gain: Gain(retiring.Track, retiring.Item)));
        });
    }
    public AudioPlaybackTicket Play(AudioConfiguration configuration, AudioPlayRequest request)
    {
        lock (_gate) return _state.Update(() =>
        {
            var ticket = new AudioPlaybackTicket(StopTicket);
            if (request.LoopCount < 0 || !Enum.IsDefined(request.Completion) || !Enum.IsDefined(request.Placement) ||
                !float.IsFinite(request.Gain) || request.Gain < 0 || !float.IsFinite(request.FadeInSeconds) || request.FadeInSeconds < 0 ||
                !float.IsFinite(request.FadeOutSeconds) || request.FadeOutSeconds < 0)
            { Report("Invalid audio playback request."); ticket.Complete(); return ticket; }
            var track = GetTrack(configuration, request.TrackId);
            if (track is null || string.IsNullOrWhiteSpace(request.AssetId) || _catchUp && !track.Definition.PreserveDuringCatchUp)
            { ticket.Complete(); return ticket; }
            if (request.Placement == AudioPlacement.ReplaceIfDifferent)
            {
                var same = track.State.Active.Where(item => item.Request.AssetId == request.AssetId).ToArray();
                if (same.Length > 0)
                {
                    foreach (var item in same)
                    {
                        if (item.Request.LoopCount != request.LoopCount) item.CompletedIterations = 0;
                        item.Request = item.Request with { Gain = request.Gain, LoopCount = request.LoopCount, Completion = request.Completion };
                        SendItem(track, item, AudioBackendAction.SetGain);
                    }
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
                    StopActive(track, null);
                    if (request.Placement == AudioPlacement.ReplaceAll) ClearPending(track);
                    Start(track, value); break;
                case AudioPlacement.Next:
                    if (track.Definition.MaxVoices != 1) { Report("audio.next requires a single-voice track."); Complete(value); break; }
                    StopActive(track, null); QueueOrStart(track, value, true); Pump(track); break;
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
            if (request.FadeOutSeconds is { } fade && (!float.IsFinite(fade) || fade < 0)) { Report("Invalid audio fade-out duration."); return; }
            var track = GetTrack(configuration, request.TrackId); if (track is null) return;
            foreach (var retiring in _retiring.Values.Where(value => value.Binding.TrackId == request.TrackId).ToArray())
                _dispatcher.Enqueue(new(AudioBackendAction.Stop, retiring.Binding.TrackId, retiring.Binding.Id,
                    retiring.Binding.Generation, FadeSeconds: request.FadeOutSeconds ?? retiring.Item.Request.FadeOutSeconds));
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
            StopActive(track, null); Pump(track);
        });
    }
    public void Clear(AudioConfiguration configuration, string trackId)
    { lock (_gate) _state.Update(() => { var track = GetTrack(configuration, trackId); if (track is not null) ClearPending(track); }); }
    public void ApplySEffect(AudioConfiguration configuration, AudioSEffectRequest request)
    {
        lock (_gate) _state.Update(() =>
        {
            Configure(configuration);
            if (!configuration.HasTarget(request.Scope, request.TargetId)) { Report("Unknown audio effect target."); return; }
            if (request.Scope == SEffectScope.Bus && _host?.Capabilities.SupportsMixer == false) { Report("Audio backend does not support bus effects."); return; }
            var track = request.Scope == SEffectScope.Track ? GetTrack(configuration, request.TargetId) : null;
            var effect = new AudioSEffectState(request.TargetId, request.InstanceId, request.SEffectId, request.Parameters.Clone(), request.Scope);
            try { ValidateEffect(effect); } catch (InvalidDataException error) { Report(error.Message); return; }
            var effects = _state.Table<AudioSEffectState>();
            if (!effects.TryGet(effect.Key(), out _) && effects.Values.Count(value => value.Scope == effect.Scope && value.TargetId == effect.TargetId) >= 64)
            { Report("An audio target supports at most 64 active effects."); return; }
            effects.Set(effect);
            if (_host?.Capabilities.SupportsMixer == true) SendTargetEffect(effect, AudioBackendAction.ApplySEffect);
            else if (track is not null) foreach (var item in track.State.Active.ToArray()) SendEffect(track, item, effect, AudioBackendAction.ApplySEffect);
        });
    }
    public void StopSEffect(AudioConfiguration configuration, string trackId, string instanceId, SEffectScope scope = SEffectScope.Track)
    {
        lock (_gate) _state.Update(() =>
        {
            Configure(configuration);
            var track = scope == SEffectScope.Track ? GetTrack(configuration, trackId) : null;
            var table = _state.Table<AudioSEffectState>();
            var key = new AudioSEffectState(trackId, instanceId, "", default, scope).Key();
            if (!table.TryGet(key, out var effect)) return;
            table.Remove(key);
            if (_host?.Capabilities.SupportsMixer == true) SendTargetEffect(effect, AudioBackendAction.StopSEffect);
            else if (track is not null) foreach (var item in track.State.Active.ToArray()) SendEffect(track, item, effect, AudioBackendAction.StopSEffect);
        });
    }
    private TrackBinding? GetTrack(AudioConfiguration configuration, string id)
    {
        if (_disposed) return null;
        Configure(configuration);
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
            StopVoice(track, track.State.Active[0], null); Start(track, item); return;
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
        {
            var index = 0;
            if (track.Definition.PlaybackOrder == AudioPlaybackOrder.Random)
            {
                var seed = track.State.RandomState;
                seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
                track.State.RandomState = seed == 0 ? 0x9e3779b9 : seed;
                index = (int)(track.State.RandomState % (uint)track.State.Pending.Count);
            }
            var item = track.State.Pending[index]; track.State.Pending.RemoveAt(index); Start(track, item);
        }
    }
    private void Project(TrackBinding track, AudioItemState item, float fade)
    {
        if (_catchUp || track.State.Paused || _host is null) return;
        var generation = ++_nextGeneration;
        var binding = new VoiceBinding(track.State.Id, $"{track.Definition.Id}:{generation}", generation);
        _voices[item.Id] = binding;
        _dispatcher.Enqueue(new(AudioBackendAction.Start, track.Definition.Id, binding.Id, generation, item.Request.AssetId, Gain(track, item), fade, FadeOutSeconds: item.Request.FadeOutSeconds));
        if (_host.Capabilities.SupportsMixer) return;
        foreach (var effect in _state.Table<AudioSEffectState>().Values.Where(value => value.Scope == SEffectScope.Track && value.TargetId == track.State.Id))
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
        if (_host?.Capabilities.SupportsMixer == true) return item.Request.Gain;
        var group = track.Definition.BusId is { } busId ? _mix.Buses.GetValueOrDefault(busId) : null;
        _mix.Tracks.TryGetValue(track.Definition.Id, out var trackMix);
        if (_mix.MasterMuted || group?.Muted == true || trackMix?.Muted == true) return 0;
        var gain = (double)track.Definition.DefaultGain * item.Request.Gain * (group?.Gain ?? 1f) * (trackMix?.Gain ?? 1f) * _mix.MasterGain;
        return (float)Math.Clamp(gain, 0, 1);
    }
    private void ResetMixer()
    {
        _configuration = null;
        if (_host?.Capabilities.SupportsMixer == true) _dispatcher.Enqueue(new(AudioBackendAction.ResetMixer, "", "", 0));
    }
    private void Configure(AudioConfiguration configuration)
    {
        _declarations = configuration;
        if (ReferenceEquals(_configuration, configuration)) return;
        _configuration = configuration;
        if (_host?.Capabilities.SupportsMixer != true) return;
        _dispatcher.Enqueue(new(AudioBackendAction.Configure, "", "", 0, Configuration: configuration));
        _dispatcher.Enqueue(new(AudioBackendAction.SetMix, "", "", 0, Mix: _mix));
        foreach (var channel in _state.Table<AudioChannelState>().Values) SendLevel(channel);
        foreach (var effect in _state.Table<AudioSEffectState>().Values) SendTargetEffect(effect, AudioBackendAction.ApplySEffect);
    }
    private void SendTargetEffect(AudioSEffectState effect, AudioBackendAction action)
    {
        if (!_catchUp) _dispatcher.Enqueue(new(action, effect.TargetId, "", 0, SEffectId: effect.SEffectId,
            SEffectInstanceId: effect.InstanceId, SEffectParameters: effect.Parameters, Scope: effect.Scope));
    }
    public void SetLevel(AudioConfiguration configuration, AudioChannelState channel)
    {
        lock (_gate) _state.Update(() =>
        {
            Configure(configuration);
            ValidateChannel(channel);
            if (!configuration.HasTarget(channel.Scope, channel.TargetId)) { Report("Unknown audio level target."); return; }
            if (_host?.Capabilities.SupportsMixer == false) { Report("Audio backend does not support target mixer levels."); return; }
            _state.Table<AudioChannelState>().Set(channel);
            if (!_catchUp) SendLevel(channel);
        });
    }
    private void SendLevel(AudioChannelState channel) => _dispatcher.Enqueue(new(AudioBackendAction.SetLevel,
        channel.TargetId, "", 0, Gain: channel.Gain, Muted: channel.Muted, Scope: channel.Scope));
    private List<AudioPlaybackTicket> Tickets(string id)
    { if (!_tickets.TryGetValue(id, out var tickets)) _tickets[id] = tickets = []; return tickets; }
    private void Complete(AudioItemState item)
    { if (_tickets.Remove(item.Id, out var tickets)) foreach (var ticket in tickets) ticket.Complete(); }
    private void StopVoice(TrackBinding track, AudioItemState item, float? requestedFade)
    {
        var fade = requestedFade ?? item.Request.FadeOutSeconds;
        track.State.Active.Remove(item);
        if (_voices.Remove(item.Id, out var binding))
        {
            if (fade > 0 && _host?.Capabilities.SupportsFade == true)
                _retiring[binding.Id] = new(track, item, binding);
            _dispatcher.Enqueue(new(AudioBackendAction.Stop, binding.TrackId, binding.Id, binding.Generation, FadeSeconds: Math.Max(0, fade)));
        }
        Complete(item);
    }
    private void StopActive(TrackBinding track, float? fade) { foreach (var item in track.State.Active.ToArray()) StopVoice(track, item, fade); }
    private void ClearPending(TrackBinding track) { foreach (var item in track.State.Pending) Complete(item); track.State.Pending.Clear(); }
    private void StopBindings()
    {
        var bindings = _voices.Values.Concat(_retiring.Values.Select(value => value.Binding)).ToArray();
        _voices.Clear();
        _retiring.Clear();
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
            if (_disposed || value.Kind is not (AudioBackendEventKind.Ended or AudioBackendEventKind.Failed)) return;
            if (_retiring.TryGetValue(value.VoiceId, out var retiring) && retiring.Binding.TrackId == value.TrackId && retiring.Binding.Generation == value.Generation)
            { _retiring.Remove(value.VoiceId); _dispatcher.CompleteVoice(value.VoiceId); return; }
            if (!_tracks.TryGetValue(value.TrackId, out var track)) return;
            var item = track.State.Active.FirstOrDefault(item => _voices.TryGetValue(item.Id, out var voice) && voice.Id == value.VoiceId && voice.Generation == value.Generation);
            if (item is null) return;
            _voices.Remove(item.Id); _dispatcher.CompleteVoice(value.VoiceId);
            if (value.Kind == AudioBackendEventKind.Ended &&
                (track.Definition.Repeat == AudioTrackRepeat.One || item.Request.LoopCount == 0 || item.CompletedIterations < item.Request.LoopCount - 1))
            {
                if (track.Definition.Repeat != AudioTrackRepeat.One && item.Request.LoopCount > 0) item.CompletedIterations++;
                Project(track, item, 0);
                return;
            }
            track.State.Active.Remove(item);
            if (value.Kind == AudioBackendEventKind.Failed) Report(value.Message ?? $"Audio backend failed for '{item.Request.AssetId}'.");
            if (value.Kind == AudioBackendEventKind.Ended && (item.Request.Completion == AudioCompletion.RequeueTail || track.Definition.Repeat == AudioTrackRepeat.All))
            {
                item.CompletedIterations = 0;
                if (track.State.Pending.Count == 0) Start(track, item);
                else
                {
                    // Free a voice for the existing queue before applying the idle queue budget.
                    track.State.Pending.Add(item); Pump(track);
                    if (track.State.Pending.Count > track.Definition.MaxQueuedItems)
                    {
                        var index = track.Definition.QueueOverflowPolicy == AudioQueueOverflowPolicy.DropNewest ? track.State.Pending.Count - 1 : 0;
                        var dropped = track.State.Pending[index]; track.State.Pending.RemoveAt(index); Complete(dropped);
                        Report("Audio queue limit reached while requeuing completed playback.");
                    }
                }
                return;
            }
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
                foreach (var item in track.State.Active.Where(item => Tickets(item.Id).Contains(ticket)).ToArray()) StopVoice(track, item, null);
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
            StopBindings(); ResetMixer(); if (_host is not null) _host.EventReceived -= OnBackendEvent;
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
    private sealed record RetiringVoice(TrackBinding Track, AudioItemState Item, VoiceBinding Binding);
    private sealed record VoiceBinding(string TrackId, string Id, long Generation);
}
