using System.Collections.Frozen;
using GalNet.Core.Parameters;
using System.Text.Json;

namespace GalNet.Contracts.Builtins.Audio;

/// <summary>The attachment targets supported by an audio effect descriptor.</summary>
public enum SEffectScope { Track, Bus }

/// <summary>Metadata for one numeric parameter accepted by a sound effect.</summary>
public sealed record SEffectParameterDefinition(
    string Name,
    bool Required = false,
    float? Minimum = null,
    float? Maximum = null);

/// <summary>Platform-neutral authoring metadata for one named audio effect.</summary>
public sealed record SEffectDefinition(
    string Id,
    IReadOnlySet<SEffectScope> SupportedScopes,
    IReadOnlyList<SEffectParameterDefinition> Parameters);

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
                parameter => ParameterValidation.Field(parameter.Name, typeof(float), parameter.Required, parameter.Minimum, parameter.Maximum), StringComparer.Ordinal),
            StringComparer.OrdinalIgnoreCase);
        Definitions = _byId.Values.OrderBy(definition => definition.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static SEffectCatalog Empty { get; } = new([]);
    public static SEffectCatalog Builtins { get; } = new([
        new("seffect.gain", Scopes(), [new("gain", Required: true, Minimum: 0, Maximum: 1)]),
        new("seffect.lowPass", Scopes(), [new("cutoffHz", Required: true, Minimum: 20, Maximum: 20_000), new("q", Minimum: .1f, Maximum: 10)]),
        new("seffect.highPass", Scopes(), [new("cutoffHz", Required: true, Minimum: 20, Maximum: 20_000), new("q", Minimum: .1f, Maximum: 10)]),
        new("seffect.reverb", Scopes(), [new("roomSize", Minimum: 0, Maximum: 1), new("decaySeconds", Minimum: .1f, Maximum: 20), new("damping", Minimum: 0, Maximum: 1), new("mix", Minimum: 0, Maximum: 1)])]);
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
