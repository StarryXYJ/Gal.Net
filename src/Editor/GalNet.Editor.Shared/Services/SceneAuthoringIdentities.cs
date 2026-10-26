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
            parameters[output.Field] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString("N"));
        return parameters;
    }
    public static string? CreationSlot(string type) => SceneAuthoringSemantics.Find(type)?.Outputs.FirstOrDefault(slot => slot.Domain == SceneIdentityDomain.Object)?.Field;
    public static bool IsOutput(string type, string field) => SceneAuthoringSemantics.Find(type)?.Outputs.Any(slot => slot.Field == field) == true;
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
