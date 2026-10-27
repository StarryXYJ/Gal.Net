using System.Text.Json;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Media.Audio;

namespace GalNet.Media.Audio.Tests;

public sealed class PcmMixerTests
{
    private static AudioConfiguration Configuration => new([new() { Id = "a", BusId = "room" }, new() { Id = "b", BusId = "room" }], [new() { Id = "room" }]);
    private static AudioSEffectState Effect(string target, string id, string parameters, SEffectScope scope = SEffectScope.Track, string instance = "fx") =>
        new(target, instance, id, JsonSerializer.Deserialize<JsonElement>(parameters), scope);
    private static void Add(PcmMixer mixer, string track, IAudioPcmSource source, float gain = 1, float fadeIn = 0, float fadeOut = 0) =>
        mixer.Add(new(new(AudioBackendAction.Start, track, Guid.NewGuid().ToString(), 1, Gain: gain, FadeSeconds: fadeIn, FadeOutSeconds: fadeOut), mixer.Target(SEffectScope.Track, track), source));
    private static float[] Render(PcmMixer mixer, int frames = 48_000)
    { var output = new float[frames * 2]; mixer.Render(output); return output; }
    private static double Rms(float[] samples, int skip = 12_000) => Math.Sqrt(samples.Skip(skip).Select(sample => (double)sample * sample).Average());

    [TestCase("seffect.lowPass", 200f, 8000f)]
    [TestCase("seffect.highPass", 8000f, 100f)]
    public void Filter_has_real_passband_and_continues_rejecting_beyond_one_octave(string id, float pass, float reject)
    {
        double Measure(float frequency)
        {
            var mixer = new PcmMixer(); mixer.Configure(Configuration);
            mixer.Target(SEffectScope.Track, "a").ApplyEffect(Effect("a", id, """{"cutoffHz":1000,"q":0.7071068}"""));
            Add(mixer, "a", new Sine(frequency)); return Rms(Render(mixer));
        }
        Assert.That(Measure(pass), Is.GreaterThan(.06));
        Assert.That(Measure(reject), Is.LessThan(.002));
    }

    [Test]
    public void Bus_chain_processes_the_sum_once_and_track_effect_is_local()
    {
        var mixer = new PcmMixer(); mixer.Configure(Configuration);
        mixer.Target(SEffectScope.Track, "a").ApplyEffect(Effect("a", "seffect.gain", """{"gain":0.5}"""));
        mixer.Target(SEffectScope.Bus, "room").ApplyEffect(Effect("room", "seffect.gain", """{"gain":0.5}""", SEffectScope.Bus));
        Add(mixer, "a", new Constant(.2f)); Add(mixer, "b", new Constant(.2f));
        Assert.That(Render(mixer)[^2], Is.EqualTo(.15f).Within(.0001));
    }

    [Test]
    public void Reverb_keeps_a_tail_after_the_source_has_ended_and_master_mute_covers_it()
    {
        var mixer = new PcmMixer(); mixer.Configure(Configuration);
        mixer.Target(SEffectScope.Bus, "room").ApplyEffect(Effect("room", "seffect.reverb", """{"roomSize":0.6,"decaySeconds":2,"damping":0.3,"mix":0.5}""", SEffectScope.Bus));
        Render(mixer, 1000); // Complete the effect's bypass ramp before the impulse.
        Add(mixer, "a", new Impulse());
        var output = Render(mixer, 48_000);
        Assert.That(mixer.Voices.Single().Ended, Is.True);
        Assert.That(output.Skip(12_000).Max(MathF.Abs), Is.GreaterThan(.00001));
        mixer.ApplyMix(new() { MasterMuted = true });
        Assert.That(Render(mixer, 2000).Skip(1000).All(sample => sample == 0), Is.True);
        mixer.Reset(); mixer.Configure(Configuration);
        Assert.That(Render(mixer, 2000).All(sample => sample == 0), Is.True);
    }

    [Test]
    public void Applying_updating_and_removing_an_effect_never_restarts_a_voice_or_breaks_the_signal()
    {
        var mixer = new PcmMixer(); mixer.Configure(Configuration);
        var source = new Constant(.1f); Add(mixer, "a", source);
        var target = mixer.Target(SEffectScope.Track, "a");
        Render(mixer, 1000);
        target.ApplyEffect(Effect("a", "seffect.lowPass", """{"cutoffHz":2000}"""));
        var first = Render(mixer, 2000);
        target.ApplyEffect(Effect("a", "seffect.lowPass", """{"cutoffHz":500}"""));
        var second = Render(mixer, 2000);
        target.StopEffect("fx"); var third = Render(mixer, 2000);
        var output = first.Concat(second).Concat(third).ToArray();
        Assert.That(output.All(sample => float.IsFinite(sample) && sample > .08f), Is.True);
        Assert.That(source.ReadSamples, Is.EqualTo(14_000));
        Assert.That(mixer.Voices.Single().Ended, Is.False);
    }

    [Test]
    public void Effects_are_ordered_and_updates_preserve_their_position()
    {
        double Measure(bool gainFirst)
        {
            var mixer = new PcmMixer(); mixer.Configure(Configuration); var channel = mixer.Target(SEffectScope.Track, "a");
            var gain = Effect("a", "seffect.gain", """{"gain":0}""", instance: "gain");
            var reverb = Effect("a", "seffect.reverb", """{"mix":1,"decaySeconds":2}""", instance: "verb");
            channel.ApplyEffect(gainFirst ? gain : reverb); channel.ApplyEffect(gainFirst ? reverb : gain);
            Render(mixer, 1000);
            // Build a reverb tail, then close gain without moving the existing effect.
            channel.ApplyEffect(gain with { Parameters = JsonSerializer.Deserialize<JsonElement>("{\"gain\":1}") });
            Add(mixer, "a", new Sine(400)); Render(mixer, 10_000);
            channel.ApplyEffect(gain); return Rms(Render(mixer, 8000), 2000);
        }
        Assert.That(Measure(true), Is.GreaterThan(.0001));
        Assert.That(Measure(false), Is.LessThan(.000001));
    }

    [Test]
    public void Instance_fades_are_independent_and_pause_then_stop_retires()
    {
        var mixer = new PcmMixer(); mixer.Configure(Configuration);
        Add(mixer, "a", new Constant(.2f), fadeIn: .02f);
        var output = Render(mixer, 1920);
        Assert.That(output[0], Is.Zero); Assert.That(output[1920], Is.EqualTo(.2f).Within(.001));
        var voice = mixer.Voices.Single(); voice.Paused = true; voice.Stop(.01f);
        Assert.That(Render(mixer, 1024).All(sample => sample == 0), Is.True);
        Assert.That(voice.Ended, Is.True);
    }

    [Test]
    public void Stop_then_reapply_appends_an_effect_and_type_updates_leave_only_one_active_instance()
    {
        var mixer = new PcmMixer(); mixer.Configure(Configuration);
        var channel = mixer.Target(SEffectScope.Track, "a");
        var gain = Effect("a", "seffect.gain", "{\"gain\":1}", instance: "gate");
        channel.ApplyEffect(gain);
        channel.ApplyEffect(Effect("a", "seffect.reverb", "{\"mix\":1}"));
        Add(mixer, "a", new Sine(400)); Render(mixer, 10_000);
        channel.StopEffect("gate");
        channel.ApplyEffect(gain with { Parameters = JsonSerializer.Deserialize<JsonElement>("{\"gain\":0}") });
        Assert.That(Rms(Render(mixer, 8000), 2000), Is.LessThan(.000001), "Re-applied gain is after the existing reverb, and must mute its tail.");

        mixer.Reset(); mixer.Configure(Configuration); channel = mixer.Target(SEffectScope.Track, "a");
        Add(mixer, "a", new Constant(.1f));
        channel.ApplyEffect(Effect("a", "seffect.lowPass", "{\"cutoffHz\":1000}")); Render(mixer, 1000);
        channel.ApplyEffect(Effect("a", "seffect.gain", "{\"gain\":0.5}")); Render(mixer, 1000);
        channel.ApplyEffect(Effect("a", "seffect.lowPass", "{\"cutoffHz\":500}"));
        Assert.That(Render(mixer, 2000)[^2], Is.EqualTo(.1f).Within(.0001));
    }

    [Test]
    public void Steady_state_render_does_not_allocate()
    {
        var mixer = new PcmMixer(); mixer.Configure(Configuration);
        mixer.Target(SEffectScope.Bus, "room").ApplyEffect(Effect("room", "seffect.reverb", "{}", SEffectScope.Bus));
        Add(mixer, "a", new Sine(440)); var buffer = new float[2048];
        for (var index = 0; index < 10; index++) mixer.Render(buffer);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 100; index++) mixer.Render(buffer);
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - allocated, Is.Zero);
    }

    private sealed class Sine(float frequency) : IAudioPcmSource
    {
        private long _frame;
        public double DurationSeconds => 0;
        public bool IsFinished => false;
        public int Read(Span<float> samples)
        { for (var index = 0; index < samples.Length; index += 2) samples[index] = samples[index + 1] = .1f * MathF.Sin(2 * MathF.PI * frequency * _frame++ / PcmMixer.SampleRate); return samples.Length; }
    }
    private sealed class Constant(float value) : IAudioPcmSource
    {
        public int ReadSamples { get; private set; }
        public double DurationSeconds => 0;
        public bool IsFinished => false;
        public int Read(Span<float> samples) { samples.Fill(value); ReadSamples += samples.Length; return samples.Length; }
    }
    private sealed class Impulse : IAudioPcmSource
    {
        private bool _done;
        public double DurationSeconds => 1d / PcmMixer.SampleRate;
        public bool IsFinished => _done;
        public int Read(Span<float> samples) { if (_done) return 0; samples[0] = samples[1] = .5f; _done = true; return 2; }
    }
}
