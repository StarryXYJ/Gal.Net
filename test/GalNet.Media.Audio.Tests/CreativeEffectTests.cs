using System.Text.Json;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Media.Audio;

namespace GalNet.Media.Audio.Tests;

public sealed class CreativeEffectTests
{
    private static PcmMixer Mixer(string effect, string json)
    {
        var mixer = new PcmMixer(); mixer.Configure(new([new() { Id = "a" }]));
        mixer.Target(SEffectScope.Track, "a").ApplyEffect(new("a", "fx", effect, JsonSerializer.Deserialize<JsonElement>(json)));
        mixer.Render(new float[2048]);
        return mixer;
    }
    private static void Source(PcmMixer mixer, IAudioPcmSource source) => mixer.Add(new(new(AudioBackendAction.Start, "a", "voice", 1), mixer.Target(SEffectScope.Track, "a"), source));

    [TestCase("seffect.delay")] [TestCase("seffect.pan")] [TestCase("seffect.eq")]
    [TestCase("seffect.distortion")] [TestCase("seffect.bitcrusher")] [TestCase("seffect.compressor")]
    public void Omitted_parameters_match_the_authoring_catalog_defaults(string id)
    {
        SEffectCatalog.Builtins.TryGet(id, out var definition);
        var implicitDefaults = Mixer(id, "{}"); var explicitDefaults = Mixer(id, definition.DefaultParameters.GetRawText());
        Source(implicitDefaults, new Sine(1300)); Source(explicitDefaults, new Sine(1300));
        var a = new float[4096]; var b = new float[4096]; implicitDefaults.Render(a); explicitDefaults.Render(b);
        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void Duck_waits_for_a_rendered_voice_multiplies_story_and_player_gain_and_releases_on_pause()
    {
        var mixer = new PcmMixer();
        var configuration = new AudioConfiguration([new() { Id = "a", BusId = "music" }, new() { Id = "vocal" }], [new() { Id = "music" }]);
        mixer.Configure(configuration);
        mixer.SetDucking([new("voice-duck", "vocal", "music", Gain: .25f, AttackMs: 5, ReleaseMs: 20)]);
        mixer.Target(SEffectScope.Bus, "music").ContentLevel = new(.5f);
        mixer.ApplyMix(new() { Buses = new Dictionary<string, AudioMixLevel> { ["music"] = new(.5f) } });
        Source(mixer, new Stereo(.2f, .2f));
        var output = new float[48000]; mixer.Render(output);
        Assert.That(output[^2], Is.EqualTo(.05f).Within(.001));
        var voice = new PcmVoice(new(AudioBackendAction.Start, "vocal", "spoken", 1), mixer.Target(SEffectScope.Track, "vocal"), new Stereo(0, 0));
        mixer.Add(voice); mixer.Render(output);
        Assert.That(output[^2], Is.EqualTo(.0125f).Within(.001));
        voice.Paused = true; mixer.Render(output);
        Assert.That(output[^2], Is.EqualTo(.05f).Within(.001));
        var before = GC.GetAllocatedBytesForCurrentThread(); mixer.Render(output);
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }

    [Test]
    public void Delay_emits_at_configured_time_retains_feedback_tail_and_reset_clears_it()
    {
        var mixer = Mixer("seffect.delay", """{"delaySeconds":0.01,"feedback":0.5,"mix":1}""");
        Source(mixer, new Impulse()); var output = new float[4800]; mixer.Render(output);
        Assert.That(output[960], Is.EqualTo(.5f).Within(.001));
        Assert.That(output[1920], Is.EqualTo(.25f).Within(.001));
        Assert.That(output.Take(960).All(x => x == 0), Is.True);
        mixer.Reset(); mixer.Configure(new([new() { Id = "a" }])); mixer.Render(output);
        Assert.That(output.All(x => x == 0), Is.True);
    }

    [TestCase(-1, .2f, 0)] [TestCase(0, .2f, .1f)] [TestCase(1, 0, .1f)]
    public void Pan_is_stereo_balance(float pan, float left, float right)
    {
        var mixer = Mixer("seffect.pan", JsonSerializer.Serialize(new { pan })); Source(mixer, new Stereo(.2f, .1f));
        var output = new float[4096]; mixer.Render(output);
        Assert.That(output[^2], Is.EqualTo(left).Within(.0001)); Assert.That(output[^1], Is.EqualTo(right).Within(.0001));
    }

    [Test]
    public void Peak_eq_boosts_the_requested_band()
    {
        double Measure(float frequency)
        {
            var mixer = Mixer("seffect.eq", """{"frequencyHz":1000,"q":2,"gainDb":12}""");
            Source(mixer, new Sine(frequency)); var buffer = new float[96000]; mixer.Render(buffer);
            return Math.Sqrt(buffer.Skip(24000).Average(x => x * x));
        }
        Assert.That(Measure(1000), Is.GreaterThan(Measure(100) * 3));
    }

    [Test]
    public void Linked_compressor_reduces_level_and_preserves_stereo_ratio()
    {
        var mixer = Mixer("seffect.compressor", """{"thresholdDb":-20,"ratio":10,"attackMs":1,"releaseMs":50}""");
        Source(mixer, new Stereo(.8f, .4f)); var output = new float[48000]; mixer.Render(output);
        Assert.That(output[^2], Is.LessThan(.2)); Assert.That(output[^2] / output[^1], Is.EqualTo(2).Within(.001));
    }

    [TestCase("seffect.delay")] [TestCase("seffect.pan")] [TestCase("seffect.eq")]
    [TestCase("seffect.distortion")] [TestCase("seffect.bitcrusher")] [TestCase("seffect.compressor")]
    public void Catalog_defaults_and_extremes_produce_finite_signal_without_callback_allocation(string id)
    {
        SEffectCatalog.Builtins.TryGet(id, out var definition);
        foreach (var maximum in new[] { false, true })
        {
            var parameters = definition.Parameters.ToDictionary(x => x.Name, x => maximum ? x.Maximum ?? x.DefaultValue!.Value : x.Minimum ?? x.DefaultValue!.Value);
            var mixer = Mixer(id, JsonSerializer.Serialize(parameters)); Source(mixer, new Sine(1300)); var buffer = new float[2048];
            for (var i = 0; i < 100; i++) mixer.Render(buffer);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++) mixer.Render(buffer);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero); Assert.That(buffer.All(float.IsFinite), Is.True);
        }
    }

    private sealed class Stereo(float left, float right) : IAudioPcmSource
    {
        public double DurationSeconds => 0;
        public bool IsFinished => false;
        public int Read(Span<float> samples) { for (var i = 0; i < samples.Length; i += 2) { samples[i] = left; samples[i + 1] = right; } return samples.Length; }
    }
    private sealed class Sine(float frequency) : IAudioPcmSource
    {
        private int _frame;
        public double DurationSeconds => 0;
        public bool IsFinished => false;
        public int Read(Span<float> samples) { for (var i = 0; i < samples.Length; i += 2) samples[i] = samples[i + 1] = MathF.Sin(2 * MathF.PI * frequency * _frame++ / 48000) * .05f; return samples.Length; }
    }
    private sealed class Impulse : IAudioPcmSource
    {
        private bool _read;
        public double DurationSeconds => 0;
        public bool IsFinished => _read;
        public int Read(Span<float> samples) { samples.Clear(); if (_read) return 0; _read = true; samples[0] = samples[1] = .5f; return 2; }
    }
}
