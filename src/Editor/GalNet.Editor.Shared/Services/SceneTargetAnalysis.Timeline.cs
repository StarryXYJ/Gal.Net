using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Services;

public sealed partial class SceneTargetAnalysis
{
    private static readonly JsonSerializerOptions TimelineJson = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
    private const int TimelineBudget = 10000;
    private static JsonElement? Member(JsonElement value, string name)
    {
        var matches = value.EnumerateObject().Where(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1) throw new InvalidDataException("Ambiguous Timeline member.");
        return matches.Length == 0 ? null : matches[0].Value;
    }
    private static (TimelineDefinition Timeline, JsonElement Raw) ReadTimeline(EditorEntryData entry)
    {
        if (!entry.Parameters.TryGetValue("timeline", out var raw) || raw.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Missing Timeline.");
        foreach (var name in new[] { "tracks", "events", "frameRate", "durationFrames", "loopMode" }) _ = Member(raw, name);
        var timeline = raw.Deserialize<TimelineDefinition>(TimelineJson) ?? throw new InvalidDataException("Missing Timeline.");
        TimelineEventCapabilities.ValidateStructure(timeline);
        if (timeline.Events.Count + timeline.Tracks.Sum(track => (long)track.Keys.Count) > TimelineBudget) throw new InvalidDataException("Timeline query budget exceeded.");
        if (Member(raw, "events") is { } events)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in events.EnumerateArray())
            {
                foreach (var name in new[] { "frame", "type", "parameters", "execution" }) _ = Member(item, name);
                if (Member(item, "editorId") is { } identity && (identity.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(identity.GetString()) || !ids.Add(identity.GetString()!)))
                    throw new InvalidDataException("Ambiguous event identity.");
            }
        }
        return (timeline, raw);
    }
    private static SceneTargetResult Result(State state) => new(state.Targets.Values.OrderBy(target => target.Locator.GroupId, StringComparer.Ordinal)
        .ThenBy(target => target.Locator.EntryId, StringComparer.Ordinal).ThenBy(target => target.CreatedAtFrame).ThenBy(target => target.Locator.EventId, StringComparer.Ordinal).ToArray(), state.Complete && state.Targets.Values.All(target => target.CapabilitiesComplete), true)
    { Playbacks = state.Playbacks.Values.OrderBy(target => target.Locator.GroupId, StringComparer.Ordinal).ThenBy(target => target.Locator.EntryId, StringComparer.Ordinal).ToArray() };
    public SceneTargetResult AtTimeline(string groupId, string entryId, SceneTimelinePosition position)
    {
        var state = StateBefore(groupId, entryId);
        if (state is null || !_groups.TryGetValue(groupId, out var entries) || entries.FirstOrDefault(entry => entry.StableId == entryId) is not { Type: BuiltinEntryIds.PlayTimeline } entry)
            return new([], false, false);
        try
        {
            var (timeline, raw) = ReadTimeline(entry);
            if (position.Frame < 0 || position.Frame > timeline.DurationFrames || position.BeforeEventIndex is { } index &&
                (index < 0 || index >= timeline.Events.Count || timeline.Events[index].Frame != position.Frame)) return new([], false, false);
            EvaluateTimeline(state, groupId, entry, timeline, raw, position, terminal: false);
            AddPlayback(state, groupId, entry, timeline.PlaybackHandleId, "timeline.playbackHandleId",
                timeline.DurationFrames == 0 || position.Frame == timeline.DurationFrames ? TargetPresence.Possible : TargetPresence.Definite);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or InvalidDataException or OverflowException) { state.Uncertain(); }
        return Result(state);
    }
    private void TransferTimeline(State state, string group, EditorEntryData entry, bool requireValidExpansion = false)
    {
        var (timeline, raw) = ReadTimeline(entry); var before = state.Clone();
        EvaluateTimeline(state, group, entry, timeline, raw, new(timeline.DurationFrames), terminal: true);
        if (requireValidExpansion && !state.Complete) throw new InvalidDataException("Incomplete transition expansion.");
        if (timeline.LoopMode == AnimationLoopMode.Once && (timeline.Blocking || timeline.DurationFrames == 0)) return;
        // Later story positions cannot assume concurrent or cyclic presentation has finished.
        var merged = Merge([before, state]);
        // An object may exist between creation and removal even when absent at both endpoints.
        for (var index = 0; index < timeline.Events.Count; index++)
        {
            var item = timeline.Events[index];
            if (TimelineEventCapabilities.CreationSlot(item.Type) is null) continue;
            try
            {
                var candidate = new State(); ApplyEvent(candidate, group, entry, item, index, raw, TimelineEventCapabilities.NormalizeEvent(item));
                foreach (var pair in candidate.Targets) merged.Targets.TryAdd(pair.Key, pair.Value);
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or InvalidDataException or OverflowException) { merged.Complete = false; }
        }
        if (requireValidExpansion && !merged.Complete) throw new InvalidDataException("Incomplete transition expansion.");
        state.Targets.Clear(); foreach (var pair in merged.Targets) state.Targets.Add(pair.Key, pair.Value);
        state.Playbacks.Clear(); foreach (var pair in merged.Playbacks) state.Playbacks.Add(pair.Key, pair.Value);
        state.Uncertain();
        AddPlayback(state, group, entry, timeline.PlaybackHandleId, "timeline.playbackHandleId", TargetPresence.Possible);
    }
    private void EvaluateTimeline(State state, string group, EditorEntryData owner, TimelineDefinition timeline, JsonElement raw, SceneTimelinePosition position, bool terminal)
    {
        var ordered = timeline.Events.Select((item, index) => (Item: item, Index: index)).OrderBy(pair => pair.Item.Frame).ThenBy(pair => pair.Index).ToArray();
        var touched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (item, index) in ordered.Where(pair => (terminal || pair.Item.Execution != TimelineEventExecution.OnCompletion) &&
            (pair.Item.Frame < position.Frame || pair.Item.Frame == position.Frame && (position.BeforeEventIndex is null || pair.Index < position.BeforeEventIndex))))
        {
            try
            {
                var parameters = TimelineEventCapabilities.NormalizeEvent(item);
                foreach (var name in new[] { "handleId", "instanceId" }) if (parameters.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String) touched.Add(value.GetString()!);
                ApplyEvent(state, group, owner, item, index, raw, parameters);
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or InvalidDataException or OverflowException) { state.Uncertain(); }
        }
        foreach (var track in timeline.Tracks)
        {
            if (!state.Targets.TryGetValue(track.HandleId, out var target)) continue;
            var values = new Dictionary<string, float>(target.Values);
            var capability = target.Properties.FirstOrDefault(property => property.Name == track.Property);
            if (position.BeforeEventIndex is not null) values.Remove(track.Property);
            else
            {
                var value = TimelineSampler.Evaluate(track, position.Frame);
                var known = target.Presence == TargetPresence.Definite && capability is not null;
                if (track.BlendMode == AnimationBlendMode.Additive)
                {
                    known &= (terminal || !touched.Contains(track.HandleId)) && values.TryGetValue(track.Property, out _);
                    value = known ? value + values[track.Property] : value;
                }
                if (known && float.IsFinite(value) && capability!.Accepts(value)) values[track.Property] = value;
                else { values.Remove(track.Property); state.Complete = false; }
            }
            state.Targets[track.HandleId] = target with { Values = new ReadOnlyDictionary<string, float>(values) };
        }
        if (!position.IncludeFuture) return;
        foreach (var (item, index) in ordered.Where(pair => pair.Item.Frame > position.Frame || pair.Item.Frame == position.Frame && position.BeforeEventIndex is { } boundary && pair.Index >= boundary))
        {
            if (!TimelineEventCapabilities.Supports(item.Type)) { state.Complete = false; continue; }
            if (TimelineEventCapabilities.CreationSlot(item.Type) is null) continue;
            try
            {
                var candidate = new State(); ApplyEvent(candidate, group, owner, item, index, raw, TimelineEventCapabilities.NormalizeEvent(item));
                foreach (var (handle, target) in candidate.Targets)
                    state.Targets.TryAdd(handle, target with { Presence = TargetPresence.Possible, Values = EmptyValues, IsFuture = true });
                state.Complete &= candidate.Complete;
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or InvalidDataException or OverflowException) { state.Complete = false; }
        }
    }
    private void ApplyEvent(State state, string group, EditorEntryData owner, TimelineEventDefinition item, int index, JsonElement raw, JsonElement parameters)
    {
        Transfer(state, group, new EditorEntryData { StableId = owner.StableId, Type = item.Type, Parameters = parameters.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone()) });
        if (TimelineEventCapabilities.CreationSlot(item.Type) is not { } slot || !parameters.TryGetProperty(slot, out var output) || output.GetString() is not { } handle || !state.Targets.TryGetValue(handle, out var target)) return;
        var identity = Member(Member(raw, "events")!.Value[index], "editorId")?.GetString() ?? slot + ":" + handle;
        state.Targets[handle] = target with { Locator = new(group, owner.StableId, slot, identity), CreatedAtFrame = item.Frame, IsFuture = false };
    }
}
