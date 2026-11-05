using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Services;

/// <summary>Copies author parameters while retaining the target's declared per-entry fields and output identities.</summary>
public static class EntryPresetParameters
{
    public static Dictionary<string, JsonElement> Capture(IEntryCatalog catalog, string type, IReadOnlyDictionary<string, JsonElement> parameters)
    {
        var result = Clone(parameters);
        foreach (var field in Excluded(catalog, type)) result.Remove(field);
        foreach (var output in SceneAuthoringSemantics.Find(type)?.Outputs ?? [])
        {
            if (output.Container is null) result.Remove(output.Field);
            else if (result.TryGetValue(output.Container, out var value) && value.ValueKind == JsonValueKind.Object)
            {
                var node = JsonNode.Parse(value.GetRawText())!.AsObject();
                var key = node.Select(pair => pair.Key).FirstOrDefault(key => key.Equals(output.Field, StringComparison.OrdinalIgnoreCase));
                if (key is not null) node.Remove(key);
                result[output.Container] = JsonSerializer.SerializeToElement(node);
            }
        }
        return result;
    }

    public static Dictionary<string, JsonElement> Apply(IEntryCatalog catalog, string type, IReadOnlyDictionary<string, JsonElement> preset, IReadOnlyDictionary<string, JsonElement> current)
    {
        var result = Capture(catalog, type, preset);
        // Nested timelines can declare their own outputs and references; allocate a fresh
        // internally consistent set instead of copying the source's live identities.
        var copy = new EditorEntryData { Type = type, Parameters = result };
        SceneAuthoringIdentities.RemapCopy([copy]);
        result = copy.Parameters;
        foreach (var field in Excluded(catalog, type)) if (current.TryGetValue(field, out var value)) result[field] = value.Clone();
        foreach (var output in SceneAuthoringSemantics.Find(type)?.Outputs ?? [])
        {
            if (output.Container is null)
            {
                if (current.TryGetValue(output.Field, out var value)) result[output.Field] = value.Clone();
                continue;
            }
            if (!current.TryGetValue(output.Container, out var source) || source.ValueKind != JsonValueKind.Object) continue;
            var match = source.EnumerateObject().Where(property => property.Name.Equals(output.Field, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (match.Length != 1) continue;
            var target = result.TryGetValue(output.Container, out var container) && container.ValueKind == JsonValueKind.Object
                ? JsonNode.Parse(container.GetRawText())!.AsObject() : new JsonObject();
            target[match[0].Name] = JsonNode.Parse(match[0].Value.GetRawText());
            result[output.Container] = JsonSerializer.SerializeToElement(target);
        }
        return result;
    }

    private static IEnumerable<string> Excluded(IEntryCatalog catalog, string type) => catalog.TryGet(type, out var schema)
        ? schema.DynamicParameters.Values.Where(item => item.Field.ExcludeFromPreset).Select(item => item.Name) : [];
    private static Dictionary<string, JsonElement> Clone(IReadOnlyDictionary<string, JsonElement> values) => values.ToDictionary(item => item.Key, item => item.Value.Clone(), StringComparer.Ordinal);
}
