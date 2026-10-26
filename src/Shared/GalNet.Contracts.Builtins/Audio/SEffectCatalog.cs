using System.Text.Json;

namespace GalNet.Contracts.Builtins.Audio;

/// <summary>The attachment targets supported by an audio effect descriptor.</summary>
public enum SEffectScope { Track, Item }

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

    public SEffectCatalog(IEnumerable<SEffectDefinition> definitions)
    {
        _byId = definitions.ToDictionary(definition => definition.Id, StringComparer.OrdinalIgnoreCase);
        Definitions = _byId.Values.OrderBy(definition => definition.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static SEffectCatalog Empty { get; } = new([]);
    public static SEffectCatalog Builtins { get; } = new([
        new("seffect.gain", new HashSet<SEffectScope> { SEffectScope.Track }, [new("gain", Required: true, Minimum: 0, Maximum: 1)]),
        new("seffect.lowPass", new HashSet<SEffectScope> { SEffectScope.Track },
            [new("cutoffHz", Required: true, Minimum: 20, Maximum: 20_000), new("attenuationDb", Minimum: -20, Maximum: 0)]),
        new("seffect.highPass", new HashSet<SEffectScope> { SEffectScope.Track },
            [new("cutoffHz", Required: true, Minimum: 20, Maximum: 20_000), new("attenuationDb", Minimum: -20, Maximum: 0)])]);
    public IReadOnlyList<SEffectDefinition> Definitions { get; }
    public bool TryGet(string id, out SEffectDefinition definition) => _byId.TryGetValue(id, out definition!);

    public IReadOnlyList<string> Validate(string id, SEffectScope scope, JsonElement parameters)
    {
        if (!TryGet(id, out var definition)) return [$"Unsupported SEffect '{id}'."];
        if (!definition.SupportedScopes.Contains(scope)) return [$"SEffect '{id}' does not support {scope} scope."];
        if (parameters.ValueKind != JsonValueKind.Object) return [$"SEffect '{id}' parameters must be a JSON object."];

        var declared = definition.Parameters.ToDictionary(parameter => parameter.Name, StringComparer.Ordinal);
        var diagnostics = new List<string>();
        foreach (var value in parameters.EnumerateObject())
        {
            if (!declared.TryGetValue(value.Name, out var parameter))
            {
                diagnostics.Add($"SEffect '{id}' does not declare parameter '{value.Name}'.");
                continue;
            }
            if (value.Value.ValueKind != JsonValueKind.Number || !value.Value.TryGetSingle(out var number) || !float.IsFinite(number))
            {
                diagnostics.Add($"SEffect parameter '{value.Name}' must be a finite number.");
                continue;
            }
            if (parameter.Minimum is { } minimum && number < minimum || parameter.Maximum is { } maximum && number > maximum)
                diagnostics.Add($"SEffect parameter '{value.Name}' is outside its supported range.");
        }
        foreach (var parameter in definition.Parameters.Where(parameter => parameter.Required && !parameters.TryGetProperty(parameter.Name, out _)))
            diagnostics.Add($"SEffect '{id}' requires parameter '{parameter.Name}'.");
        return diagnostics;
    }
}
