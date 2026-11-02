namespace GalNet.Media.Audio;

internal sealed partial class PcmEffect
{
    // Owned exclusively by the audio consumer, including filter memory and delay tails.
    private sealed class CreativeProcessor
    {
        private readonly string _kind;
        private readonly float[]? _delay;
        private int _position, _heldFrames, _coefficientFrame;
        private float _mix, _pan, _feedback, _drive, _delayFrames, _gainDb, _frequency, _q;
        private float _heldLeft, _heldRight, _envelope, _compressGain = 1;
        private double _b0 = 1, _b1, _b2, _a1, _a2, _z1L, _z2L, _z1R, _z2R;

        public CreativeProcessor(string kind, Parameters initial)
        {
            _kind = kind.ToLowerInvariant();
            _mix = initial.Mix; _pan = initial.Pan; _feedback = initial.Feedback;
            _drive = initial.Drive; _delayFrames = initial.Delay * PcmMixer.SampleRate;
            _gainDb = initial.GainDb; _frequency = initial.Frequency; _q = initial.Q;
            if (_kind == "seffect.delay") _delay = new float[(PcmMixer.SampleRate * 2 + 2) * 2];
        }

        public void Process(float dryLeft, float dryRight, Parameters p, out float left, out float right)
        {
            _mix = PcmMixer.Smooth(_mix, p.Mix);
            left = dryLeft; right = dryRight;
            switch (_kind)
            {
                case "seffect.delay":
                    _delayFrames += (p.Delay * PcmMixer.SampleRate - _delayFrames) / 960;
                    _feedback = PcmMixer.Smooth(_feedback, p.Feedback);
                    var frames = _delay!.Length / 2;
                    var read = (_position - (double)_delayFrames + frames) % frames;
                    var first = (int)read; var fraction = (float)(read - first);
                    var next = (first + 1) % frames;
                    var echoLeft = _delay[first * 2] + (_delay[next * 2] - _delay[first * 2]) * fraction;
                    var echoRight = _delay[first * 2 + 1] + (_delay[next * 2 + 1] - _delay[first * 2 + 1]) * fraction;
                    _delay[_position * 2] = dryLeft + echoLeft * _feedback;
                    _delay[_position * 2 + 1] = dryRight + echoRight * _feedback;
                    _position = (_position + 1) % frames;
                    left = dryLeft * (1 - _mix) + echoLeft * _mix;
                    right = dryRight * (1 - _mix) + echoRight * _mix;
                    break;
                case "seffect.pan":
                    // Stereo balance: centre is unity; retain the favoured channel unchanged.
                    _pan = PcmMixer.Smooth(_pan, p.Pan);
                    left *= MathF.Cos(MathF.Max(0, _pan) * MathF.PI / 2);
                    right *= MathF.Cos(MathF.Max(0, -_pan) * MathF.PI / 2);
                    break;
                case "seffect.eq":
                    _gainDb += (p.GainDb - _gainDb) / 960;
                    _frequency += (p.Frequency - _frequency) / 960; _q += (p.Q - _q) / 960;
                    if ((_coefficientFrame++ & 15) == 0)
                    {
                        var amplitude = Math.Pow(10, _gainDb / 40);
                        var omega = 2 * Math.PI * _frequency / PcmMixer.SampleRate;
                        var alpha = Math.Sin(omega) / (2 * _q);
                        var cosine = Math.Cos(omega); var a0 = 1 + alpha / amplitude;
                        _b0 = (1 + alpha * amplitude) / a0; _b1 = -2 * cosine / a0;
                        _b2 = (1 - alpha * amplitude) / a0; _a1 = _b1;
                        _a2 = (1 - alpha / amplitude) / a0;
                    }
                    left = Peak(dryLeft, ref _z1L, ref _z2L);
                    right = Peak(dryRight, ref _z1R, ref _z2R);
                    break;
                case "seffect.distortion":
                    _drive += (p.Drive - _drive) / 960;
                    left = dryLeft + (MathF.Tanh(dryLeft * _drive) / MathF.Tanh(_drive) - dryLeft) * _mix;
                    right = dryRight + (MathF.Tanh(dryRight * _drive) / MathF.Tanh(_drive) - dryRight) * _mix;
                    break;
                case "seffect.bitcrusher":
                    if (_heldFrames-- <= 0)
                    {
                        var steps = MathF.Pow(2, MathF.Round(p.Bits) - 1);
                        _heldLeft = MathF.Round(Math.Clamp(dryLeft, -1, 1) * steps) / steps;
                        _heldRight = MathF.Round(Math.Clamp(dryRight, -1, 1) * steps) / steps;
                        _heldFrames = (int)MathF.Round(p.Downsample) - 1;
                    }
                    left = dryLeft + (_heldLeft - dryLeft) * _mix;
                    right = dryRight + (_heldRight - dryRight) * _mix;
                    break;
                case "seffect.compressor":
                    var peak = MathF.Max(MathF.Abs(dryLeft), MathF.Abs(dryRight));
                    var coefficient = MathF.Exp(-1 / (.001f * (peak > _envelope ? p.AttackMs : p.ReleaseMs) * PcmMixer.SampleRate));
                    _envelope = coefficient * _envelope + (1 - coefficient) * peak;
                    var over = MathF.Max(0, 20 * MathF.Log10(MathF.Max(_envelope, 1e-10f)) - p.ThresholdDb);
                    var target = MathF.Pow(10, (p.MakeupDb - over * (1 - 1 / p.Ratio)) / 20);
                    _compressGain += (target - _compressGain) / 48;
                    left *= _compressGain; right *= _compressGain;
                    break;
            }
        }

        private float Peak(float input, ref double z1, ref double z2)
        {
            var output = input * _b0 + z1;
            z1 = input * _b1 - output * _a1 + z2; z2 = input * _b2 - output * _a2;
            if (Math.Abs(z1) < 1e-25) z1 = 0;
            if (Math.Abs(z2) < 1e-25) z2 = 0;
            return (float)output;
        }
    }
}
