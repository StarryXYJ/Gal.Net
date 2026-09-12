using System.Text.Json;

namespace GalNet.Core.Scene;

/// <summary>Fixed slots in the game render pipeline. Every pixel effect consumes and produces the same image stream.</summary>
public enum EffectStage { Layer, SceneBeforeUi, SceneAfterUi }

/// <summary>Editor-facing JSON value kinds for an effect's static start parameters.</summary>
public enum EffectParameterKind { Text, Integer, Float, Boolean, ImageAsset, Select }

/// <summary>Metadata for one JSON member accepted by an effect factory.</summary>
public sealed record EffectParameterDefinition(
    string Name,
    EffectParameterKind Kind,
    bool Required = false,
    float? Minimum = null,
    float? Maximum = null,
    IReadOnlyList<string>? Options = null);

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
    private readonly Dictionary<string, IReadOnlyDictionary<string, EffectParameterDefinition>> _parametersByEffect;

    public EffectCatalog(IEnumerable<EffectDefinition> definitions)
    {
        _byId = definitions.ToDictionary(definition => definition.Id, StringComparer.OrdinalIgnoreCase);
        _parametersByEffect = _byId.Values.ToDictionary(
            definition => definition.Id,
            definition => (IReadOnlyDictionary<string, EffectParameterDefinition>)definition.Parameters.ToDictionary(parameter => parameter.Name, StringComparer.Ordinal),
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
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add("Effect parameters must be a JSON object.");
                return diagnostics;
            }

            var known = _parametersByEffect[definition.Id];
            foreach (var value in document.RootElement.EnumerateObject())
            {
                if (!known.TryGetValue(value.Name, out var parameter))
                {
                    diagnostics.Add($"Effect '{id}' does not declare parameter '{value.Name}'.");
                    continue;
                }
                ValidateValue(parameter, value.Value, diagnostics);
            }
            foreach (var parameter in definition.Parameters.Where(parameter => parameter.Required && !document.RootElement.TryGetProperty(parameter.Name, out _)))
                diagnostics.Add($"Effect '{id}' requires parameter '{parameter.Name}'.");
        }
        return diagnostics;
    }

    private static void ValidateValue(EffectParameterDefinition parameter, JsonElement value, List<string> diagnostics)
    {
        var expected = parameter.Kind switch
        {
            EffectParameterKind.Text or EffectParameterKind.ImageAsset or EffectParameterKind.Select => JsonValueKind.String,
            EffectParameterKind.Integer or EffectParameterKind.Float => JsonValueKind.Number,
            EffectParameterKind.Boolean => JsonValueKind.True,
            _ => JsonValueKind.Undefined
        };
        if (parameter.Kind == EffectParameterKind.Boolean)
        {
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return;
            diagnostics.Add($"Effect parameter '{parameter.Name}' must be Boolean.");
            return;
        }
        if (value.ValueKind != expected)
        {
            diagnostics.Add($"Effect parameter '{parameter.Name}' must be {parameter.Kind}.");
            return;
        }
        if (parameter.Kind == EffectParameterKind.Integer && !value.TryGetInt32(out _))
            diagnostics.Add($"Effect parameter '{parameter.Name}' must be an Integer.");
        if ((parameter.Kind is EffectParameterKind.Integer or EffectParameterKind.Float) && value.TryGetDouble(out var number))
        {
            if (!double.IsFinite(number) || parameter.Minimum is { } minimum && number < minimum || parameter.Maximum is { } maximum && number > maximum)
                diagnostics.Add($"Effect parameter '{parameter.Name}' is outside its supported range.");
        }
        if (parameter.Kind == EffectParameterKind.Select && parameter.Options is { Count: > 0 } options && !options.Contains(value.GetString() ?? "", StringComparer.Ordinal))
            diagnostics.Add($"Effect parameter '{parameter.Name}' has an unsupported value.");
    }
}
