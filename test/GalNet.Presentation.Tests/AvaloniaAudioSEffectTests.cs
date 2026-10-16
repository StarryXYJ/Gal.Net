using System.Text.Json;
using GalNet.Media.LibVlc;

namespace GalNet.Presentation.Tests;

[TestFixture]
public sealed class AvaloniaAudioSEffectTests
{
    [Test]
    public void Default_registry_exposes_the_processors_it_can_execute()
    {
        var registry = new LibVlcAudioSEffectRegistry(LibVlcAudioSEffectRegistry.DefaultProcessors);

        Assert.That(registry.Catalog.Definitions.Select(definition => definition.Id), Is.EquivalentTo(["seffect.gain", "seffect.lowPass", "seffect.highPass"]));
    }

    [Test]
    public void Registry_accepts_a_host_specific_processor()
    {
        var registry = new LibVlcAudioSEffectRegistry([new TestProcessor()]);

        Assert.That(registry.Catalog.TryGet("seffect.test", out _), Is.True);
        Assert.That(registry.TryGet("seffect.test", out var processor), Is.True);
        Assert.That(processor, Is.TypeOf<TestProcessor>());
    }

    [Test]
    public void Low_and_high_pass_processors_aggregate_equalizer_attenuation()
    {
        var state = new LibVlcAudioSEffectState([60, 1_000, 8_000]);
        using var lowPassParameters = JsonDocument.Parse("{\"cutoffHz\": 1000, \"attenuationDb\": -20}");
        using var highPassParameters = JsonDocument.Parse("{\"cutoffHz\": 1000, \"attenuationDb\": -20}");

        new LowPassSEffectProcessor().Apply(lowPassParameters.RootElement, state);
        new HighPassSEffectProcessor().Apply(highPassParameters.RootElement, state);

        Assert.That(state.BandGain(0), Is.EqualTo(-20).Within(.01));
        Assert.That(state.BandGain(1), Is.EqualTo(0).Within(.01));
        Assert.That(state.BandGain(2), Is.EqualTo(-20).Within(.01));
    }

    [Test]
    public void Gain_processors_multiply_the_output_gain()
    {
        var state = new LibVlcAudioSEffectState([]);
        using var first = JsonDocument.Parse("{\"gain\": 0.5}");
        using var second = JsonDocument.Parse("{\"gain\": 0.4}");

        new GainSEffectProcessor().Apply(first.RootElement, state);
        new GainSEffectProcessor().Apply(second.RootElement, state);

        Assert.That(state.Gain, Is.EqualTo(.2).Within(.001));
    }

    private sealed class TestProcessor : IAudioSEffectProcessor
    {
        public GalNet.Core.Audio.SEffectDefinition Definition { get; } = new("seffect.test", new HashSet<GalNet.Core.Audio.SEffectScope> { GalNet.Core.Audio.SEffectScope.Track }, []);
        public void Apply(JsonElement parameters, LibVlcAudioSEffectState state) { }
    }
}
