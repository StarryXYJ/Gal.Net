using System.Collections.Frozen;
using System.Text.Json;
using GalNet.Core.Parameters;
using GalNet.Core.Serialization;

namespace GalNet.Contracts.Builtins.Audio;

/// <summary>The attachment targets supported by an audio effect descriptor.</summary>
public enum SEffectScope { Track, Bus }

/// <summary>Metadata for one numeric parameter accepted by a sound effect.</summary>
public sealed record SEffectParameterDefinition(
    string Name,
    bool Required = false,
    float? Minimum = null,
    float? Maximum = null,
    float? DefaultValue = null)
{
    public FieldDescriptor Field => new(Name, ParameterValidation.Field(Name, typeof(float), Required, Minimum, Maximum).Value,
        Required, defaultValue: DefaultValue is { } value ? JsonCodec.SerializeToElement(value) : null);
}

/// <summary>Platform-neutral authoring metadata for one named audio effect.</summary>
public sealed record SEffectDefinition(
    string Id,
    IReadOnlySet<SEffectScope> SupportedScopes,
    IReadOnlyList<SEffectParameterDefinition> Parameters)
{
    public JsonElement DefaultParameters => JsonCodec.SerializeToElement(Parameters.Where(parameter => parameter.DefaultValue is not null)
        .ToDictionary(parameter => parameter.Name, parameter => parameter.DefaultValue!.Value));
}

public interface ISEffectCatalog
{
    IReadOnlyList<SEffectDefinition> Definitions { get; }
    bool TryGet(string id, out SEffectDefinition definition);
    IReadOnlyList<string> Validate(string id, SEffectScope scope, JsonElement parameters);
}

/// <summary>Immutable effect metadata registry supplied by the active playback host.</summary>
public sealed class SEffectCatalog : ISEffectCatalog
{
    private readonly IReadOnlyDictionary<string, SEffectDefinition> _byId;
    private readonly Dictionary<string, IReadOnlyDictionary<string, FieldDescriptor>> _fields;

    public SEffectCatalog(IEnumerable<SEffectDefinition> definitions)
    {
        _byId = definitions.Select(definition => definition with
        { SupportedScopes = definition.SupportedScopes.ToFrozenSet(), Parameters = Array.AsReadOnly(definition.Parameters.ToArray()) })
            .ToDictionary(definition => definition.Id, StringComparer.OrdinalIgnoreCase);
        _fields = _byId.Values.ToDictionary(definition => definition.Id,
            definition => (IReadOnlyDictionary<string, FieldDescriptor>)definition.Parameters.ToDictionary(parameter => parameter.Name,
                parameter => parameter.Field, StringComparer.Ordinal),
            StringComparer.OrdinalIgnoreCase);
        Definitions = _byId.Values.OrderBy(definition => definition.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static SEffectCatalog Empty { get; } = new([]);
    public static SEffectCatalog Builtins { get; } = new([
        new("seffect.gain", Scopes(), [new("gain", Required: true, Minimum: 0, Maximum: 1, DefaultValue: SEffectDefaults.Gain)]),
        new("seffect.lowPass", Scopes(), [new("cutoffHz", Required: true, Minimum: 20, Maximum: 20_000, DefaultValue: SEffectDefaults.LowPassCutoff), new("q", Minimum: .1f, Maximum: 10, DefaultValue: SEffectDefaults.Q)]),
        new("seffect.highPass", Scopes(), [new("cutoffHz", Required: true, Minimum: 20, Maximum: 20_000, DefaultValue: SEffectDefaults.HighPassCutoff), new("q", Minimum: .1f, Maximum: 10, DefaultValue: SEffectDefaults.Q)]),
        new("seffect.reverb", Scopes(), [new("roomSize", Minimum: 0, Maximum: 1, DefaultValue: SEffectDefaults.RoomSize), new("decaySeconds", Minimum: .1f, Maximum: 20, DefaultValue: SEffectDefaults.DecaySeconds), new("damping", Minimum: 0, Maximum: 1, DefaultValue: SEffectDefaults.Damping), new("mix", Minimum: 0, Maximum: 1, DefaultValue: SEffectDefaults.Mix)]),
        new("seffect.delay", Scopes(), [new("delaySeconds", Minimum: .001f, Maximum: 2, DefaultValue: .25f), new("feedback", Minimum: 0, Maximum: .95f, DefaultValue: .35f), new("mix", Minimum: 0, Maximum: 1, DefaultValue: .25f)]),
        new("seffect.pan", Scopes(), [new("pan", Minimum: -1, Maximum: 1, DefaultValue: 0)]),
        new("seffect.eq", Scopes(), [new("frequencyHz", Minimum: 20, Maximum: 20_000, DefaultValue: 1000), new("q", Minimum: .1f, Maximum: 10, DefaultValue: SEffectDefaults.Q), new("gainDb", Minimum: -24, Maximum: 24, DefaultValue: 0)]),
        new("seffect.distortion", Scopes(), [new("drive", Minimum: 1, Maximum: 20, DefaultValue: 2), new("mix", Minimum: 0, Maximum: 1, DefaultValue: .25f)]),
        new("seffect.bitcrusher", Scopes(), [new("bits", Minimum: 2, Maximum: 16, DefaultValue: 8), new("downsample", Minimum: 1, Maximum: 64, DefaultValue: 1), new("mix", Minimum: 0, Maximum: 1, DefaultValue: .5f)]),
        new("seffect.compressor", Scopes(), [new("thresholdDb", Minimum: -60, Maximum: 0, DefaultValue: -18), new("ratio", Minimum: 1, Maximum: 20, DefaultValue: 4), new("attackMs", Minimum: .1f, Maximum: 200, DefaultValue: 10), new("releaseMs", Minimum: 5, Maximum: 2000, DefaultValue: 100), new("makeupDb", Minimum: 0, Maximum: 24, DefaultValue: 0)])]);
    private static HashSet<SEffectScope> Scopes() => [SEffectScope.Track, SEffectScope.Bus];
    public IReadOnlyList<SEffectDefinition> Definitions { get; }
    public bool TryGet(string id, out SEffectDefinition definition) => _byId.TryGetValue(id, out definition!);

    public IReadOnlyList<string> Validate(string id, SEffectScope scope, JsonElement parameters)
    {
        if (!TryGet(id, out var definition)) return [$"Unsupported SEffect '{id}'."];
        if (!definition.SupportedScopes.Contains(scope)) return [$"SEffect '{id}' does not support {scope} scope."];
        return ParameterValidation.Validate("SEffect", id, parameters, _fields[definition.Id]);
    }
}
