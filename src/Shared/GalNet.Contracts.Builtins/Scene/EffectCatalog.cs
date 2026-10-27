using GalNet.Core.Parameters;
using System.Text.Json;

namespace GalNet.Contracts.Builtins.Scene;

/// <summary>Fixed slots in the game render pipeline. Every pixel effect consumes and produces the same image stream.</summary>
public enum EffectStage { Layer, ScenePost }

/// <summary>Editor-facing JSON value kinds for an effect's static start parameters.</summary>
public enum EffectParameterKind { Text, Integer, Float, Boolean, ImageAsset, Select }

/// <summary>Metadata for one JSON member accepted by an effect factory.</summary>
public sealed record EffectParameterDefinition(
    string Name,
    EffectParameterKind Kind,
    bool Required = false,
    float? Minimum = null,
    float? Maximum = null,
    IReadOnlyList<string>? Options = null)
{
    public FieldDescriptor Field => ParameterValidation.Field(Name, Kind switch
    { EffectParameterKind.Integer => typeof(int), EffectParameterKind.Float => typeof(float), EffectParameterKind.Boolean => typeof(bool), _ => typeof(string) },
        Required, Minimum, Maximum, Options);
}

/// <summary>Platform-neutral authoring metadata emitted by an effect factory.</summary>
public sealed record EffectDefinition(
    string Id,
    EffectStage Stage,
    IReadOnlyList<EffectParameterDefinition> Parameters,
    IReadOnlyList<AnimatableProperty> AnimatableProperties);

public interface IEffectCatalog
{
    IReadOnlyList<EffectDefinition> Definitions { get; }
    bool TryGet(string id, out EffectDefinition definition);
    IReadOnlyList<string> Validate(string id, string targetHandleId, string parameters);
}

/// <summary>
/// Immutable effect metadata registry. Validation returns diagnostics instead of throwing so
/// editor and runtime hosts can decide whether a malformed effect remains executable.
/// </summary>
public sealed class EffectCatalog : IEffectCatalog
{
    private readonly IReadOnlyDictionary<string, EffectDefinition> _byId;
    private readonly Dictionary<string, IReadOnlyDictionary<string, FieldDescriptor>> _parametersByEffect;

    public EffectCatalog(IEnumerable<EffectDefinition> definitions)
    {
        _byId = definitions.Select(definition => definition with
        {
            Parameters = Array.AsReadOnly(definition.Parameters.Select(parameter => parameter with { Options = parameter.Options is { } choices ? Array.AsReadOnly(choices.ToArray()) : null }).ToArray()),
            AnimatableProperties = Array.AsReadOnly(definition.AnimatableProperties.ToArray())
        }).ToDictionary(definition => definition.Id, StringComparer.OrdinalIgnoreCase);
        _parametersByEffect = _byId.Values.ToDictionary(
            definition => definition.Id,
            definition => (IReadOnlyDictionary<string, FieldDescriptor>)definition.Parameters.ToDictionary(parameter => parameter.Name,
                parameter => parameter.Field, StringComparer.Ordinal),
            StringComparer.OrdinalIgnoreCase);
        Definitions = _byId.Values.OrderBy(definition => definition.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<EffectDefinition> Definitions { get; }
    public bool TryGet(string id, out EffectDefinition definition) => _byId.TryGetValue(id, out definition!);

    public IReadOnlyList<string> Validate(string id, string targetHandleId, string parameters)
    {
        if (!TryGet(id, out var definition)) return [$"Unknown effect '{id}'."];
        var diagnostics = new List<string>();
        if (definition.Stage == EffectStage.Layer && string.IsNullOrWhiteSpace(targetHandleId))
            diagnostics.Add($"Layer effect '{id}' requires targetHandleId.");
        if (definition.Stage != EffectStage.Layer && !string.IsNullOrWhiteSpace(targetHandleId))
            diagnostics.Add($"{definition.Stage} effect '{id}' must not target a Layer.");

        JsonDocument document;
        try { document = JsonDocument.Parse(string.IsNullOrWhiteSpace(parameters) ? "{}" : parameters); }
        catch (JsonException error) { diagnostics.Add($"Effect parameters are not valid JSON: {error.Message}"); return diagnostics; }
        using (document)
        {
            diagnostics.AddRange(ParameterValidation.Validate("Effect", id, document.RootElement, _parametersByEffect[definition.Id]));
        }
        return diagnostics;
    }
}
