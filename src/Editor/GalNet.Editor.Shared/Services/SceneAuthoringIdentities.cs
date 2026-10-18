using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Services;

/// <summary>Explicit built-in identity slots. Never replaces unrelated author strings.</summary>
public static class SceneAuthoringIdentities
{
    public static Dictionary<string, JsonElement> NewEntry(string type, Dictionary<string, JsonElement> parameters)
    {
        if (CreationSlot(type) is { } slot) parameters[slot] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString("N"));
        if (type == BuiltinEntryIds.Animate) parameters["playbackHandleId"] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString("N"));
        return parameters;
    }
    private static string? CreationSlot(string type) => type switch
    {
        BuiltinEntryIds.ShowLayer or BuiltinEntryIds.ShowColorLayer or BuiltinEntryIds.PlayVideo => "handleId",
        BuiltinEntryIds.PlayParticleEmitter or BuiltinEntryIds.ApplyEffect => "instanceId",
        _ => null
    };
    public static void RemapCopy(IReadOnlyList<EditorEntryData> entries)
    {
        var roots = entries.Select(entry => (Entry: entry, Parameters: JsonSerializer.SerializeToNode(entry.Parameters)!.AsObject())).ToArray();
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        void Allocate(JsonObject obj, string slot)
        {
            if (obj[slot] is JsonValue value && value.TryGetValue<string>(out var id) && !string.IsNullOrWhiteSpace(id)) replacements.TryAdd(id, Guid.NewGuid().ToString("N"));
        }
        void Discover(string type, JsonObject parameters)
        {
            if (CreationSlot(type) is { } slot) Allocate(parameters, slot);
            if (type == BuiltinEntryIds.Animate) Allocate(parameters, "playbackHandleId");
            if (type != BuiltinEntryIds.PlayTimeline || parameters["timeline"] is not JsonObject timeline) return;
            foreach (var (key, _) in timeline.ToArray()) if (key.Equals("playbackHandleId", StringComparison.OrdinalIgnoreCase)) Allocate(timeline, key);
            foreach (var (key, node) in timeline) if (key.Equals("events", StringComparison.OrdinalIgnoreCase) && node is JsonArray events)
                foreach (var item in events.OfType<JsonObject>()) if (item.FirstOrDefault(pair => pair.Key.Equals("type", StringComparison.OrdinalIgnoreCase)).Value is JsonValue eventType && eventType.TryGetValue<string>(out var eventId)
                    && item.FirstOrDefault(pair => pair.Key.Equals("parameters", StringComparison.OrdinalIgnoreCase)).Value is JsonObject eventParameters) Discover(eventId, eventParameters);
        }
        void Replace(JsonObject obj, string slot)
        {
            if (obj[slot] is JsonValue value && value.TryGetValue<string>(out var id) && replacements.TryGetValue(id, out var replacement)) obj[slot] = replacement;
        }
        void Rewrite(string type, JsonObject parameters)
        {
            if (CreationSlot(type) is { } slot) Replace(parameters, slot);
            foreach (var field in ReferenceSlots(type)) Replace(parameters, field);
            if (type != BuiltinEntryIds.PlayTimeline || parameters["timeline"] is not JsonObject timeline) return;
            foreach (var (key, node) in timeline.ToArray())
            {
                if (key.Equals("playbackHandleId", StringComparison.OrdinalIgnoreCase)) Replace(timeline, key);
                if (key.Equals("tracks", StringComparison.OrdinalIgnoreCase) && node is JsonArray tracks)
                    foreach (var track in tracks.OfType<JsonObject>()) foreach (var field in track.Select(pair => pair.Key).Where(name => name.Equals("handleId", StringComparison.OrdinalIgnoreCase)).ToArray()) Replace(track, field);
                if (key.Equals("events", StringComparison.OrdinalIgnoreCase) && node is JsonArray events)
                    foreach (var item in events.OfType<JsonObject>()) if (item.FirstOrDefault(pair => pair.Key.Equals("type", StringComparison.OrdinalIgnoreCase)).Value is JsonValue eventType && eventType.TryGetValue<string>(out var eventId)
                        && item.FirstOrDefault(pair => pair.Key.Equals("parameters", StringComparison.OrdinalIgnoreCase)).Value is JsonObject eventParameters) Rewrite(eventId, eventParameters);
            }
        }
        foreach (var root in roots) Discover(root.Entry.Type, root.Parameters);
        foreach (var root in roots) { Rewrite(root.Entry.Type, root.Parameters); root.Entry.Parameters = root.Parameters.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value)); }
    }
    private static IEnumerable<string> ReferenceSlots(string type) => type switch
    {
        BuiltinEntryIds.Animate => ["handleId", "playbackHandleId"],
        BuiltinEntryIds.StopAnimation => ["playbackHandleId"],
        BuiltinEntryIds.HideLayer or BuiltinEntryIds.MoveLayer or BuiltinEntryIds.ReplaceLayer or BuiltinEntryIds.StopVideo => ["handleId"],
        BuiltinEntryIds.StopEffect or BuiltinEntryIds.StopParticleEmitter => ["instanceId"],
        BuiltinEntryIds.ApplyEffect => ["targetHandleId"],
        _ => []
    };
}
