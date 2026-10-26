using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Services;

/// <summary>Identity allocation and copying consume the same declarations as reference analysis.</summary>
public static class SceneAuthoringIdentities
{
    public static Dictionary<string, JsonElement> NewEntry(string type, Dictionary<string, JsonElement> parameters)
    {
        foreach (var output in SceneAuthoringSemantics.Find(type)?.Outputs ?? [])
            WriteOutput(parameters, output, JsonSerializer.SerializeToElement(Guid.NewGuid().ToString("N")));
        return parameters;
    }
    public static string? CreationSlot(string type) => SceneAuthoringSemantics.Find(type)?.Outputs.FirstOrDefault(slot => slot.Container is null && slot.Domain == SceneIdentityDomain.Object)?.Field;
    public static bool IsOutput(string type, string field) => SceneAuthoringSemantics.Find(type)?.Outputs.Any(slot => slot.Container is null && slot.Field == field) == true;
    public static Dictionary<string, JsonElement> ChangeType(string previousType, IReadOnlyDictionary<string, JsonElement> previous, string type, Dictionary<string, JsonElement> parameters)
    {
        NewEntry(type, parameters);
        var oldOutputs = SceneAuthoringSemantics.Find(previousType)?.Outputs ?? [];
        foreach (var output in SceneAuthoringSemantics.Find(type)?.Outputs ?? [])
        {
            var compatible = oldOutputs.SingleOrDefault(slot => slot.Role == output.Role && slot.Domain == output.Domain && slot.Kind == output.Kind);
            var identity = compatible is null ? default : ReadOutput(previous, compatible);
            if (identity.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(identity.GetString())) WriteOutput(parameters, output, identity.Clone());
        }
        return parameters;
    }
    private static JsonElement ReadOutput(IReadOnlyDictionary<string, JsonElement> parameters, SceneOutputSlot slot)
    {
        if (slot.Container is null) return parameters.GetValueOrDefault(slot.Field);
        if (!parameters.TryGetValue(slot.Container, out var container) || container.ValueKind != JsonValueKind.Object) return default;
        var matches = container.EnumerateObject().Where(property => property.Name.Equals(slot.Field, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 1 ? matches[0].Value : default;
    }
    private static void WriteOutput(Dictionary<string, JsonElement> parameters, SceneOutputSlot slot, JsonElement identity)
    {
        if (slot.Container is null) { parameters[slot.Field] = identity; return; }
        if (!parameters.TryGetValue(slot.Container, out var container) || container.ValueKind != JsonValueKind.Object) return;
        var matches = container.EnumerateObject().Where(property => property.Name.Equals(slot.Field, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1) return;
        var root = JsonNode.Parse(container.GetRawText())!.AsObject();
        root[matches.Length == 0 ? slot.Field : matches[0].Name] = JsonNode.Parse(identity.GetRawText());
        parameters[slot.Container] = JsonSerializer.SerializeToElement(root);
    }
    public static void RemapCopy(IReadOnlyList<EditorEntryData> entries)
    {
        var roots = entries.Select(entry => (Entry: entry, Parameters: JsonSerializer.SerializeToNode(entry.Parameters)!.AsObject())).ToArray();
        var slots = new List<SceneIdentitySlot>();
        foreach (var root in roots) SceneIdentityWalker.Walk(root.Entry.Type, root.Parameters, slots.Add, _ => { });
        var replacements = new Dictionary<(SceneIdentityDomain Domain, string Handle), string>();
        foreach (var slot in slots.Where(slot => slot.Output is not null))
            if (!string.IsNullOrWhiteSpace(slot.Handle)) replacements.TryAdd((slot.Domain, slot.Handle), Guid.NewGuid().ToString("N"));
        foreach (var slot in slots)
            if (slot.Handle is { } handle && replacements.TryGetValue((slot.Domain, handle), out var replacement)) slot.Owner[slot.Field] = replacement;
        foreach (var root in roots) root.Entry.Parameters = root.Parameters.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value));
    }
}
