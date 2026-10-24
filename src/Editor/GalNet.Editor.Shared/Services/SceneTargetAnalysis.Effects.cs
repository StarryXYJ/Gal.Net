using System.Text.Json;
using GalNet.Core.Scene;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Services;

public sealed partial class SceneTargetAnalysis
{
    private (List<AnimatableProperty> Properties, Dictionary<string, float> Values, bool Complete) EffectCapabilities(EditorEntryData entry)
    {
        string Text(string key) => entry.Parameters.TryGetValue(key, out var json) ? json.GetString() ?? "" : "";
        var properties = new List<AnimatableProperty> { new("progress", AnimationValueKind.Float, 0, 1) };
        var values = new Dictionary<string, float> { ["progress"] = 0 };
        var stage = string.IsNullOrWhiteSpace(Text("targetHandleId")) ? EffectStage.ScenePost : EffectStage.Layer;
        IReadOnlyList<AnimatableProperty> declared;
        var program = Text("program");
        if (!string.IsNullOrWhiteSpace(program))
        {
            if (_programs is null) return (properties, values, true); // Explicit lightweight analysis without a resource snapshot.
            if (!_programs.TryGetValue(program, out var descriptor) || descriptor is null || !descriptor.Supports(stage)) return (properties, values, false);
            declared = ShaderEffectAnimationProperties.Create(descriptor);
        }
        else
        {
            if (_effects is null) return (properties, values, true);
            if (!_effects.TryGet(Text("id"), out var definition) || definition.Stage != stage) return (properties, values, false);
            declared = definition.AnimatableProperties;
        }
        var complete = true;
        var parameters = entry.Parameters.GetValueOrDefault("parameters");
        var duplicateParameters = parameters.ValueKind == JsonValueKind.Object && parameters.EnumerateObject().GroupBy(property => property.Name, StringComparer.Ordinal).Any(group => group.Count() > 1);
        if (duplicateParameters) complete = false;
        foreach (var property in declared)
        {
            if (property.ValueKind != AnimationValueKind.Float || string.IsNullOrWhiteSpace(property.Name)
                || property.Minimum is { } min && !float.IsFinite(min) || property.Maximum is { } max && !float.IsFinite(max)
                || property.Minimum > property.Maximum) { complete = false; continue; }
            if (property.Name == "progress")
            {
                // Core supplies progress=0 in EffectRequest.AnimationValues, ahead of static/default
                // parameters. Restrict writes to values accepted by both Core and the presenter.
                var minimum = Math.Max(0, property.Minimum ?? 0);
                var maximum = Math.Min(1, property.Maximum ?? 1);
                properties.RemoveAll(existing => existing.Name == "progress");
                if (minimum <= maximum) properties.Add(new("progress", AnimationValueKind.Float, minimum, maximum));
                if (minimum > maximum || !property.Accepts(0)) { values.Remove("progress"); complete = false; }
                continue;
            }
            if (properties.Any(existing => existing.Name == property.Name)) { complete = false; continue; }
            properties.Add(property);
            if (duplicateParameters) continue;
            var value = property.DefaultValue ?? 0;
            if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty(property.Name, out var supplied))
            {
                if (supplied.ValueKind != JsonValueKind.Number || !supplied.TryGetSingle(out value) || !float.IsFinite(value)) { complete = false; continue; }
            }
            value = Math.Clamp(value, property.Minimum ?? float.NegativeInfinity, property.Maximum ?? float.PositiveInfinity);
            if (property.Accepts(value)) values[property.Name] = value; else complete = false;
        }
        return (properties, values, complete);
    }
}
