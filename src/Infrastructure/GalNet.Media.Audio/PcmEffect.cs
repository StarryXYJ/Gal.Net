using System.Text.Json;
using GalNet.Contracts.Builtins.Audio;

namespace GalNet.Media.Audio;

/// <summary>Persistent audio-thread DSP state; control thread publishes immutable parameters.</summary>
internal sealed class PcmEffect
{
    private Parameters _parameters;
    private int _enabled = 1;
    private float _blend;
    private readonly Biquad? _filter;
    private readonly Reverb? _reverb;
    private float _gain = 1;
    public string InstanceId { get; }
    public string Id { get; }
    public bool Enabled { get => Volatile.Read(ref _enabled) != 0; set => Volatile.Write(ref _enabled, value ? 1 : 0); }
    public bool IsBypassed => Volatile.Read(ref _blend) == 0;

    public PcmEffect(string instanceId, string id, JsonElement parameters)
    {
        InstanceId = instanceId; Id = id;
        _parameters = Parse(parameters);
        switch (id.ToLowerInvariant())
        {
            case "seffect.lowpass": _filter = new(false, _parameters.Cutoff, _parameters.Q); break;
            case "seffect.highpass": _filter = new(true, _parameters.Cutoff, _parameters.Q); break;
            case "seffect.reverb": _reverb = new(_parameters); break;
            case "seffect.gain": break;
            default: throw new InvalidDataException($"Unsupported PCM effect '{id}'.");
        }
    }
    public void Update(JsonElement parameters) => Volatile.Write(ref _parameters, Parse(parameters));
    private Parameters Parse(JsonElement parameters) => new(Value(parameters, "cutoffHz", Id.Equals("seffect.highPass", StringComparison.OrdinalIgnoreCase) ? SEffectDefaults.HighPassCutoff : SEffectDefaults.LowPassCutoff), Value(parameters, "q", SEffectDefaults.Q),
        Value(parameters, "gain", SEffectDefaults.Gain), Value(parameters, "roomSize", SEffectDefaults.RoomSize), Value(parameters, "decaySeconds", SEffectDefaults.DecaySeconds), Value(parameters, "damping", SEffectDefaults.Damping), Value(parameters, "mix", SEffectDefaults.Mix));
    private static float Value(JsonElement parameters, string name, float fallback) => parameters.TryGetProperty(name, out var value) ? value.GetSingle() : fallback;
    public void Process(Span<float> samples)
    {
        var enabled = Enabled;
        if (!enabled && _blend == 0) return;
        var parameters = Volatile.Read(ref _parameters);
        _reverb?.Prepare(parameters);
        for (var index = 0; index < samples.Length; index += 2)
        {
            _blend = PcmMixer.Smooth(_blend, enabled ? 1 : 0);
            var dryLeft = samples[index]; var dryRight = samples[index + 1];
            float left, right;
            if (_filter is not null) _filter.Process(dryLeft, dryRight, parameters, out left, out right);
            else if (_reverb is not null) _reverb.Process(dryLeft, dryRight, parameters, out left, out right);
            else { _gain = PcmMixer.Smooth(_gain, parameters.Gain); left = dryLeft * _gain; right = dryRight * _gain; }
            samples[index] = dryLeft + (left - dryLeft) * _blend;
            samples[index + 1] = dryRight + (right - dryRight) * _blend;
        }
    }
    private sealed record Parameters(float Cutoff, float Q, float Gain, float Room, float Decay, float Damping, float Mix);

    // RBJ low/high pass, transposed direct form II, separate state for each channel.
    private sealed class Biquad(bool highPass, float cutoff, float q)
    {
        private double _cutoff = cutoff, _q = q;
        private double _b0, _b1, _b2, _a1, _a2, _z1L, _z2L, _z1R, _z2R;
        private int _counter;
        public void Process(float left, float right, Parameters target, out float resultLeft, out float resultRight)
        {
            _cutoff += (target.Cutoff - _cutoff) / 960;
            _q += (target.Q - _q) / 960;
            if ((_counter++ & 15) == 0)
            {
                var omega = 2 * Math.PI * Math.Clamp(_cutoff, 20, PcmMixer.SampleRate * .49) / PcmMixer.SampleRate;
                var cosine = Math.Cos(omega); var alpha = Math.Sin(omega) / (2 * Math.Clamp(_q, .1, 10));
                var a0 = 1 + alpha;
                _b0 = (highPass ? 1 + cosine : 1 - cosine) / (2 * a0);
                _b1 = (highPass ? -(1 + cosine) : 1 - cosine) / a0;
                _b2 = _b0; _a1 = -2 * cosine / a0; _a2 = (1 - alpha) / a0;
            }
            resultLeft = Filter(left, ref _z1L, ref _z2L); resultRight = Filter(right, ref _z1R, ref _z2R);
        }
        private float Filter(float value, ref double z1, ref double z2)
        {
            var output = _b0 * value + z1;
            z1 = _b1 * value - _a1 * output + z2; z2 = _b2 * value - _a2 * output;
            if (Math.Abs(z1) < 1e-25) z1 = 0;
            if (Math.Abs(z2) < 1e-25) z2 = 0;
            return (float)output;
        }
    }

    // Four parallel damped combs per channel, followed by two all-pass diffusers.
    private sealed class Reverb
    {
        private readonly Delay[][] _combs = [CreateCombs(0), CreateCombs(.0007f)];
        private readonly AllPass[][] _diffusers = [CreateDiffusers(0), CreateDiffusers(23)];
        private readonly float[] _feedback = new float[4];
        private float _room, _damping, _mix;
        public Reverb(Parameters parameters) { _room = parameters.Room; _damping = parameters.Damping; _mix = parameters.Mix; }
        private static Delay[] CreateCombs(float offset) => new[] { .0297f, .0371f, .0411f, .0437f }.Select(seconds => new Delay(seconds + offset)).ToArray();
        private static AllPass[] CreateDiffusers(int offset) => [new(241 + offset), new(83 + offset)];
        public void Prepare(Parameters parameters)
        {
            for (var index = 0; index < 4; index++)
                _feedback[index] = MathF.Pow(.001f, _combs[0][index].Seconds * (.5f + _room * 1.5f) / Math.Clamp(parameters.Decay, .1f, 20));
        }
        public void Process(float left, float right, Parameters parameters, out float resultLeft, out float resultRight)
        {
            _room += (parameters.Room - _room) / 2400;
            _damping += (parameters.Damping - _damping) / 960;
            _mix = PcmMixer.Smooth(_mix, parameters.Mix);
            // Feed both channels into the space, retaining a stereo difference.
            var wetLeft = Channel((left * .8f + right * .2f) * .25f, 0);
            var wetRight = Channel((right * .8f + left * .2f) * .25f, 1);
            resultLeft = left * (1 - _mix) + wetLeft * _mix;
            resultRight = right * (1 - _mix) + wetRight * _mix;
        }
        private float Channel(float input, int channel)
        {
            var wet = 0f;
            for (var index = 0; index < 4; index++) wet += _combs[channel][index].Process(input, .5f + _room * 1.5f, _feedback[index], _damping * .95f);
            foreach (var diffuser in _diffusers[channel]) wet = diffuser.Process(wet);
            return wet;
        }
        private sealed class Delay(float seconds)
        {
            private readonly float[] _buffer = new float[(int)(PcmMixer.SampleRate * .1f) + 2];
            private int _position;
            private float _low;
            private float _feedback;
            public float Seconds { get; } = seconds;
            public float Process(float input, float scale, float feedback, float damping)
            {
                var read = _position - (double)Seconds * PcmMixer.SampleRate * scale;
                if (read < 0) read += _buffer.Length;
                if (read >= _buffer.Length) read -= _buffer.Length;
                var first = (int)read; var fraction = read - first;
                var output = (float)(_buffer[first] + (_buffer[(first + 1) % _buffer.Length] - _buffer[first]) * fraction);
                _low = output * (1 - damping) + _low * damping;
                if (MathF.Abs(_low) < 1e-20f) _low = 0;
                _feedback += (feedback - _feedback) / 960;
                _buffer[_position] = input + _low * _feedback;
                if (++_position == _buffer.Length) _position = 0;
                return output;
            }
        }
        private sealed class AllPass(int length)
        {
            private readonly float[] _buffer = new float[length];
            private int _position;
            public float Process(float input)
            {
                var buffered = _buffer[_position];
                var output = buffered - input;
                _buffer[_position] = input + buffered * .5f;
                if (++_position == _buffer.Length) _position = 0;
                return output;
            }
        }
    }
}
