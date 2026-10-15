using System.Text.Json;
using GalNet.Core.Audio;

namespace GalNet.Avalonia.GameView.Services;

/// <summary>Platform processor registered by an Avalonia audio host for one stable SEffect ID.</summary>
public interface IAudioSEffectProcessor
{
    SEffectDefinition Definition { get; }
    void Apply(JsonElement parameters, AvaloniaAudioSEffectState state);
}

/// <summary>Immutable construction-time registry for the effect processors a host can execute.</summary>
public sealed class AvaloniaAudioSEffectRegistry
{
    private readonly IReadOnlyDictionary<string, IAudioSEffectProcessor> _byId;

    public AvaloniaAudioSEffectRegistry(IEnumerable<IAudioSEffectProcessor> processors)
    {
        _byId = processors.ToDictionary(processor => processor.Definition.Id, StringComparer.OrdinalIgnoreCase);
        Catalog = new SEffectCatalog(_byId.Values.Select(processor => processor.Definition));
    }

    public ISEffectCatalog Catalog { get; }
    public static IReadOnlyList<IAudioSEffectProcessor> DefaultProcessors { get; } = [new GainSEffectProcessor(), new LowPassSEffectProcessor(), new HighPassSEffectProcessor()];
    public bool TryGet(string id, out IAudioSEffectProcessor processor) => _byId.TryGetValue(id, out processor!);
}

/// <summary>Aggregates output-gain and LibVLC equalizer contributions from active track effects.</summary>
public sealed class AvaloniaAudioSEffectState(IReadOnlyList<float> equalizerBandFrequencies)
{
    private readonly float[] _bandGains = new float[equalizerBandFrequencies.Count];

    public IReadOnlyList<float> EqualizerBandFrequencies { get; } = equalizerBandFrequencies;
    public float Gain { get; private set; } = 1;
    public bool HasEqualizerAdjustment => _bandGains.Any(gain => MathF.Abs(gain) > .001f);
    public float BandGain(int index) => _bandGains[index];

    public void MultiplyGain(float gain) => Gain *= Math.Clamp(gain, 0, 1);
    public void AttenuateAbove(float cutoffHz, float attenuationDb) => Attenuate(cutoffHz, attenuationDb, above: true);
    public void AttenuateBelow(float cutoffHz, float attenuationDb) => Attenuate(cutoffHz, attenuationDb, above: false);

    private void Attenuate(float cutoffHz, float attenuationDb, bool above)
    {
        for (var index = 0; index < EqualizerBandFrequencies.Count; index++)
        {
            var frequency = EqualizerBandFrequencies[index];
            if (frequency <= 0 || above && frequency <= cutoffHz || !above && frequency >= cutoffHz) continue;
            var octaves = above ? MathF.Log2(frequency / cutoffHz) : MathF.Log2(cutoffHz / frequency);
            _bandGains[index] = Math.Clamp(_bandGains[index] + attenuationDb * Math.Clamp(octaves, 0, 1), -20, 20);
        }
    }
}

public sealed class GainSEffectProcessor : IAudioSEffectProcessor
{
    public SEffectDefinition Definition { get; } = SEffectCatalog.Builtins.Definitions.Single(value => value.Id == "seffect.gain");
    public void Apply(JsonElement parameters, AvaloniaAudioSEffectState state) => state.MultiplyGain(Value(parameters, "gain", 1));
    internal static float Value(JsonElement parameters, string name, float fallback) => parameters.TryGetProperty(name, out var value) && value.TryGetSingle(out var result) ? result : fallback;
}

public sealed class LowPassSEffectProcessor : IAudioSEffectProcessor
{
    public SEffectDefinition Definition { get; } = SEffectCatalog.Builtins.Definitions.Single(value => value.Id == "seffect.lowPass");
    public void Apply(JsonElement parameters, AvaloniaAudioSEffectState state) => state.AttenuateAbove(GainSEffectProcessor.Value(parameters, "cutoffHz", 20_000), GainSEffectProcessor.Value(parameters, "attenuationDb", -20));
}

public sealed class HighPassSEffectProcessor : IAudioSEffectProcessor
{
    public SEffectDefinition Definition { get; } = SEffectCatalog.Builtins.Definitions.Single(value => value.Id == "seffect.highPass");
    public void Apply(JsonElement parameters, AvaloniaAudioSEffectState state) => state.AttenuateBelow(GainSEffectProcessor.Value(parameters, "cutoffHz", 20), GainSEffectProcessor.Value(parameters, "attenuationDb", -20));
}
