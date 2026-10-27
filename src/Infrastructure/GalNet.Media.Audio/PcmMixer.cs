using GalNet.Contracts.Builtins.Audio;

namespace GalNet.Media.Audio;

/// <summary>Single audio consumer; control calls are serialized by the owning host.</summary>
public sealed class PcmMixer
{
    public const int SampleRate = 48_000;
    private const int BlockSamples = 2048;
    private Graph _graph = new([], [], []);
    private float _master = 1;
    private float _masterTarget = 1;
    public IReadOnlyList<PcmVoice> Voices => Volatile.Read(ref _graph).Voices;

    public void Configure(AudioConfiguration configuration)
    {
        var buses = configuration.Buses.Select(bus => new PcmChannel(bus.Id, bus.DefaultGain)).ToArray();
        var tracks = configuration.Tracks.Select(track => new PcmChannel(track.Id, track.DefaultGain,
            buses.FirstOrDefault(bus => bus.Id == track.BusId))).ToArray();
        if (Voices.Count > 0) throw new InvalidOperationException("Reset active audio before replacing its configuration.");
        Volatile.Write(ref _graph, new(tracks, buses, []));
    }
    public void Reset()
    {
        Volatile.Write(ref _graph, new([], [], []));
    }
    public void Add(PcmVoice voice)
    { var graph = Volatile.Read(ref _graph); Volatile.Write(ref _graph, graph with { Voices = [.. graph.Voices, voice] }); }
    public void Remove(PcmVoice voice)
    { var graph = Volatile.Read(ref _graph); Volatile.Write(ref _graph, graph with { Voices = graph.Voices.Where(item => !ReferenceEquals(item, voice)).ToArray() }); }
    public PcmChannel Target(SEffectScope scope, string id)
    {
        var graph = Volatile.Read(ref _graph);
        return (scope == SEffectScope.Track ? graph.Tracks : scope == SEffectScope.Bus ? graph.Buses : [])
            .FirstOrDefault(target => target.Id == id) ?? throw new InvalidDataException($"Unknown {scope} audio target '{id}'.");
    }
    public void ApplyMix(AudioMixOptions options)
    {
        options = options.Snapshot();
        Volatile.Write(ref _masterTarget, options.MasterMuted ? 0 : options.MasterGain);
        var graph = Volatile.Read(ref _graph);
        foreach (var track in graph.Tracks) track.PlayerLevel = options.Tracks.GetValueOrDefault(track.Id) ?? new();
        foreach (var bus in graph.Buses) bus.PlayerLevel = options.Buses.GetValueOrDefault(bus.Id) ?? new();
    }
    public void Render(Span<float> output)
    {
        output.Clear();
        if (output.Length % 2 != 0) return;
        var graph = Volatile.Read(ref _graph);
        var voices = graph.Voices;
        while (!output.IsEmpty)
        {
            var block = output[..Math.Min(output.Length, BlockSamples)];
            foreach (var track in graph.Tracks) track.Buffer.AsSpan(0, block.Length).Clear();
            foreach (var bus in graph.Buses) bus.Buffer.AsSpan(0, block.Length).Clear();
            foreach (var voice in voices) voice.Render(block.Length);
            foreach (var track in graph.Tracks)
            {
                track.Process(block.Length);
                Add(track.Buffer.AsSpan(0, block.Length), track.Bus is { } bus ? bus.Buffer.AsSpan(0, block.Length) : block);
            }
            foreach (var bus in graph.Buses) { bus.Process(block.Length); Add(bus.Buffer.AsSpan(0, block.Length), block); }
            var masterTarget = Volatile.Read(ref _masterTarget);
            for (var index = 0; index < block.Length; index += 2)
            {
                _master = Smooth(_master, masterTarget);
                block[index] = Limit(block[index] * _master);
                block[index + 1] = Limit(block[index + 1] * _master);
            }
            output = output[block.Length..];
        }
    }
    private static float Limit(float value) => float.IsNaN(value) ? 0 : Math.Clamp(value, -1, 1);
    internal static float Smooth(float value, float target) => value + Math.Clamp(target - value, -1f / 480, 1f / 480);
    private static void Add(ReadOnlySpan<float> source, Span<float> target)
    { for (var index = 0; index < source.Length; index++) target[index] += source[index]; }
    private sealed record Graph(PcmChannel[] Tracks, PcmChannel[] Buses, PcmVoice[] Voices);
}

public sealed class PcmChannel(string id, float defaultGain = 1, PcmChannel? bus = null)
{
    private PcmEffect[] _effects = [];
    private AudioMixLevel _playerLevel = new();
    private AudioMixLevel _contentLevel = new();
    private float _gain = defaultGain;
    private readonly float _defaultGain = defaultGain;
    public string Id { get; } = id;
    public PcmChannel? Bus { get; } = bus;
    internal float[] Buffer { get; } = new float[2048];
    public AudioMixLevel PlayerLevel { set => Volatile.Write(ref _playerLevel, value); }
    public AudioMixLevel ContentLevel { set => Volatile.Write(ref _contentLevel, value); }

    public void ApplyEffect(AudioSEffectState state)
    {
        var effects = Volatile.Read(ref _effects);
        var existing = effects.FirstOrDefault(effect => effect.Enabled && effect.InstanceId == state.InstanceId && StringComparer.OrdinalIgnoreCase.Equals(effect.Id, state.SEffectId));
        if (existing is not null)
        {
            foreach (var effect in effects)
                if (effect.InstanceId == state.InstanceId && !ReferenceEquals(effect, existing)) effect.Enabled = false;
            existing.Update(state.Parameters); existing.Enabled = true; return;
        }
        var processor = new PcmEffect(state.InstanceId, state.SEffectId, state.Parameters);
        // A changed type crossfades the old instance out, then the replacement in at the same position.
        var previous = effects.FirstOrDefault(effect => effect.InstanceId == state.InstanceId && effect.Enabled);
        if (previous is not null) previous.Enabled = false;
        var list = effects.Where(effect => effect.Enabled || !effect.IsBypassed).ToList();
        if (list.Count(effect => effect.Enabled) >= 64) throw new InvalidOperationException("An audio target supports at most 64 active effect nodes.");
        var position = previous is null ? list.Count : list.IndexOf(previous) + 1;
        list.Insert(position, processor);
        Volatile.Write(ref _effects, list.ToArray());
    }
    public void StopEffect(string instanceId)
    { foreach (var effect in Volatile.Read(ref _effects)) if (effect.InstanceId == instanceId) effect.Enabled = false; }
    internal void Process(int count)
    {
        var samples = Buffer.AsSpan(0, count);
        foreach (var effect in Volatile.Read(ref _effects)) effect.Process(samples);
        var player = Volatile.Read(ref _playerLevel);
        var content = Volatile.Read(ref _contentLevel);
        var gain = player.Muted || content.Muted ? 0 : (float)Math.Min((double)_defaultGain * player.Gain * content.Gain, float.MaxValue);
        for (var index = 0; index < count; index += 2)
        {
            _gain = PcmMixer.Smooth(_gain, gain);
            samples[index] *= _gain; samples[index + 1] *= _gain;
        }
    }
}

public sealed class PcmVoice(AudioBackendRequest request, PcmChannel track, IAudioPcmSource source)
{
    private readonly float[] _buffer = new float[2048];
    private float _gain = request.Gain;
    private float _targetGain = request.Gain;
    private int _paused;
    private int _stopFrames = -1;
    private int _stopRemaining = -1;
    private int _stopTotal;
    private long _position;
    private int _ended;
    public AudioBackendRequest Request { get; } = request;
    public bool Ended => Volatile.Read(ref _ended) != 0;
    public void Abandon() => Volatile.Write(ref _ended, 1);
    public bool Paused { set => Volatile.Write(ref _paused, value ? 1 : 0); }
    public float Gain { set => Volatile.Write(ref _targetGain, value); }
    public void Stop(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        Volatile.Write(ref _stopFrames, (int)Math.Min((double)seconds * PcmMixer.SampleRate, int.MaxValue));
    }
    internal void Render(int count)
    {
        if (Ended) return;
        var stop = Interlocked.Exchange(ref _stopFrames, -1);
        if (stop >= 0) { _stopRemaining = stop; _stopTotal = stop; }
        if (_stopRemaining == 0) { Volatile.Write(ref _ended, 1); return; }
        // A stopped paused voice must still retire; its source position remains paused.
        if (Volatile.Read(ref _paused) != 0)
        {
            if (_stopRemaining > 0) _stopRemaining = Math.Max(0, _stopRemaining - count / 2);
            if (_stopRemaining == 0) Volatile.Write(ref _ended, 1);
            return;
        }
        var read = source.Read(_buffer.AsSpan(0, count));
        var fadeIn = Request.FadeSeconds * PcmMixer.SampleRate;
        var fadeOut = Request.FadeOutSeconds * PcmMixer.SampleRate;
        var length = source.DurationSeconds * PcmMixer.SampleRate;
        var targetGain = Volatile.Read(ref _targetGain);
        for (var index = 0; index < read; index += 2)
        {
            double envelope = fadeIn > 0 ? Math.Min(1, _position / fadeIn) : 1;
            if (fadeOut > 0 && length > 0) envelope *= Math.Clamp((length - _position) / fadeOut, 0, 1);
            if (_stopRemaining >= 0) envelope *= _stopTotal > 0 ? (float)_stopRemaining / _stopTotal : 0;
            _gain = PcmMixer.Smooth(_gain, targetGain);
            var gain = (float)(envelope * _gain);
            track.Buffer[index] += _buffer[index] * gain; track.Buffer[index + 1] += _buffer[index + 1] * gain;
            _position++;
            if (_stopRemaining > 0) _stopRemaining--;
            if (_stopRemaining == 0) break;
        }
        if (_stopRemaining > 0 && read < count) _stopRemaining = Math.Max(0, _stopRemaining - (count - read) / 2);
        if (_stopRemaining == 0 || source.IsFinished) Volatile.Write(ref _ended, 1);
    }
}
