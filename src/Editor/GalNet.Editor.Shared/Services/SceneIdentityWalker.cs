using System.Text.Json.Nodes;
using GalNet.Contracts.Builtins;

namespace GalNet.Editor.Shared.Services;

internal sealed record SceneIdentitySlot(JsonObject Owner, string Field, string Pointer, SceneIdentityDomain Domain, SceneOutputSlot? Output, SceneReferenceSlot? Reference, SceneTimelinePosition? Position, string? EventId)
{
    public string? Handle => Owner[Field] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    public string? Qualifier => Domain == SceneIdentityDomain.AudioEffect ? AudioAuthoringTarget.Qualifier(Owner) : null;
    public string? RequiredProperty { get; init; }
}
internal sealed record SceneCoverageGap(string Pointer, string Code);

/// <summary>Walks only declared slots; unknown JSON is preserved, never searched for matching strings.</summary>
internal static class SceneIdentityWalker
{
    public static void Walk(string type, JsonObject parameters, Action<SceneIdentitySlot> slot, Action<SceneCoverageGap> gap,
        string pointer = "/parameters", SceneTimelinePosition? position = null, string? eventId = null, int depth = 0, Action? step = null)
    {
        step?.Invoke();
        if (depth > 8) { gap(new(pointer, "scene.analysis.limit")); return; }
        var semantics = SceneAuthoringSemantics.Find(type);
        if (semantics is null) { gap(new(pointer, "scene.coverage.unknown")); return; }
        foreach (var output in semantics.Outputs.Where(output => output.Container is null)) Visit(parameters, output.Field, pointer, output.Domain, output, null, position, eventId, slot, gap);
        foreach (var reference in semantics.References)
        {
            if (reference.Domain == SceneIdentityDomain.AudioEffect && parameters["all"] is JsonValue all && all.TryGetValue<bool>(out var stopAll) && stopAll) continue;
            if (reference.Container is null) Visit(parameters, reference.Field, pointer, reference.Domain, null, reference, position, eventId, slot, gap);
            else if (parameters[reference.Container] is JsonArray array)
                for (var index = 0; index < array.Count; index++)
                {
                    step?.Invoke();
                    var path = pointer + "/" + Escape(reference.Container) + "/" + index;
                    if (array[index] is JsonObject item) Visit(item, reference.Field, path, reference.Domain, null, reference, position, eventId, slot, gap);
                    else gap(new(path, "scene.coverage.structure"));
                }
            else gap(new(pointer + "/" + reference.Container, "scene.coverage.structure"));
        }
        if (!semantics.Timeline) return;
        if (parameters["timeline"] is not JsonObject timeline) { gap(new(pointer + "/timeline", "scene.coverage.structure")); return; }
        var root = pointer + "/timeline";
        var playback = Key(timeline, "playbackHandleId", root, gap);
        if (playback is not null) Visit(timeline, playback, root, SceneIdentityDomain.Playback, semantics.Outputs.Single(output => output.Container == "timeline") with { Field = playback }, null, position, eventId, slot, gap);
        var tracks = Key(timeline, "tracks", root, gap);
        if (tracks is not null && timeline[tracks] is JsonArray trackArray)
            for (var index = 0; index < trackArray.Count; index++)
            {
                step?.Invoke();
                var path = root + "/" + Escape(tracks) + "/" + index;
                if (trackArray[index] is not JsonObject track) { gap(new(path, "scene.coverage.structure")); continue; }
                var handle = Key(track, "handleId", path, gap);
                var propertyKey = Key(track, "property", path, gap);
                var property = propertyKey is not null && track[propertyKey] is JsonValue propertyValue && propertyValue.TryGetValue<string>(out var propertyText) ? propertyText : "";
                if (handle is not null) Visit(track, handle, path, SceneIdentityDomain.Object, null, new(handle, SceneIdentityDomain.Object, [SceneTargetKind.Layer, SceneTargetKind.Particle, SceneTargetKind.Effect]), new(0), null, slot, gap, property);
            }
        else if (tracks is not null) gap(new(root + "/" + Escape(tracks), "scene.coverage.structure"));
        var events = Key(timeline, "events", root, gap);
        if (events is not null && timeline[events] is JsonArray eventArray)
            for (var index = 0; index < eventArray.Count; index++)
            {
                step?.Invoke();
                var path = root + "/" + Escape(events) + "/" + index;
                if (eventArray[index] is not JsonObject item) { gap(new(path, "scene.coverage.structure")); continue; }
                var typeKey = Key(item, "type", path, gap); var parametersKey = Key(item, "parameters", path, gap);
                var frameKey = Key(item, "frame", path, gap); var idKey = Key(item, "editorId", path, gap);
                var frame = frameKey is not null && item[frameKey] is JsonValue frameValue && frameValue.TryGetValue<int>(out var number) && number >= 0 ? number : (int?)null;
                var id = idKey is not null && item[idKey] is JsonValue idValue && idValue.TryGetValue<string>(out var text) ? text : null;
                if (typeKey is not null && item[typeKey] is JsonValue typeValue && typeValue.TryGetValue<string>(out var eventType) && parametersKey is not null && item[parametersKey] is JsonObject eventParameters)
                {
                    // Nested Timelines are not executable event semantics. Still discover their declared slots for copy/index.
                    if (!TimelineEventCapabilities.Supports(eventType) || frame is null) gap(new(path, "scene.coverage.structure"));
                    Walk(eventType, eventParameters, slot, gap, path + "/" + Escape(parametersKey), frame is { } f ? new(f, index) : new(-1), id, depth + 1, step);
                }
                else gap(new(path, "scene.coverage.structure"));
            }
        else if (events is not null) gap(new(root + "/" + Escape(events), "scene.coverage.structure"));
    }
    private static void Visit(JsonObject owner, string field, string pointer, SceneIdentityDomain domain, SceneOutputSlot? output, SceneReferenceSlot? reference, SceneTimelinePosition? position, string? eventId, Action<SceneIdentitySlot> slot, Action<SceneCoverageGap> gap, string? requiredProperty = null)
    {
        if (!owner.ContainsKey(field)) return;
        var value = new SceneIdentitySlot(owner, field, pointer + "/" + Escape(field), domain, output, reference, position, eventId) { RequiredProperty = requiredProperty };
        if (value.Handle is null) gap(new(value.Pointer, "scene.coverage.structure"));
        slot(value);
    }
    private static string? Key(JsonObject owner, string name, string pointer, Action<SceneCoverageGap> gap)
    {
        var matches = owner.Select(pair => pair.Key).Where(key => key.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1) { gap(new(pointer + "/" + name, "scene.coverage.structure")); return null; }
        return matches.FirstOrDefault();
    }
    private static string Escape(string text) => text.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
