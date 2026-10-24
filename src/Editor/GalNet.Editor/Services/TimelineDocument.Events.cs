using System.Text.Json;
using System.Text.Json.Nodes;

namespace GalNet.Editor.Services;

internal sealed record TimelineEventItem(int Index, int Frame, string Type, JsonElement Raw);

internal sealed partial class TimelineDocument
{
    public IReadOnlyList<TimelineEventItem> Events { get; }
    private IReadOnlyList<TimelineEventItem> ReadEvents() => System.Array.AsReadOnly(Array(_root, "events").Select((node, index) =>
    {
        var item = node as JsonObject ?? throw new InvalidDataException("Timeline.Invalid");
        var type = item[Name(item, "type")] is JsonValue value && value.TryGetValue<string>(out var text) ? text ?? "" : "";
        return new TimelineEventItem(index, Integer(item, "frame", 0), type, JsonSerializer.SerializeToElement(item));
    }).ToArray());
    public JsonElement AddEvent(string type, int frame, Dictionary<string, JsonElement> parameters)
    {
        if (!GalNet.Contracts.Builtins.TimelineEventCapabilities.Supports(type) || frame < 0 || frame > Duration) throw new InvalidDataException("Timeline.EditRejected");
        var root = (JsonObject)_root.DeepClone(); var name = Name(root, "events");
        if (!root.ContainsKey(name)) root[name] = new JsonArray();
        Array(root, "events").Add(new JsonObject { ["frame"] = frame, ["type"] = type, ["editorId"] = Guid.NewGuid().ToString("N"), ["parameters"] = JsonSerializer.SerializeToNode(parameters) });
        return JsonSerializer.SerializeToElement(root);
    }
    public JsonElement MoveEvents(IEnumerable<int> indices, int delta)
    {
        var selected = indices.Distinct().ToArray(); var events = Events;
        ValidateEventMove(selected, delta);
        var root = (JsonObject)_root.DeepClone(); var items = Array(root, "events"); var duration = Duration;
        foreach (var index in selected) { var item = (JsonObject)items[index]!; var frame = checked(events[index].Frame + delta); item[Name(item, "frame")] = frame; duration = Math.Max(duration, frame); }
        root[Name(root, "durationFrames")] = duration;
        return JsonSerializer.SerializeToElement(root);
    }
    public void ValidateEventMove(IEnumerable<int> indices, int delta)
    {
        var selected = indices.Distinct().ToArray();
        if (selected.Length == 0 || selected.Any(index => index < 0 || index >= Events.Count || (long)Events[index].Frame + delta < 0 || (long)Events[index].Frame + delta > int.MaxValue)) throw new InvalidDataException("Timeline.EditRejected");
    }
    public JsonElement DeleteEvents(IEnumerable<int> indices)
    {
        var selected = indices.Distinct().OrderDescending().ToArray();
        if (selected.Any(index => index < 0 || index >= Events.Count)) throw new InvalidDataException("Timeline.EditRejected");
        var root = (JsonObject)_root.DeepClone(); foreach (var index in selected) Array(root, "events").RemoveAt(index);
        return JsonSerializer.SerializeToElement(root);
    }
    public JsonElement OrderEvent(int index, int direction)
    {
        var events = Events; if (index < 0 || index >= events.Count || direction is not (-1 or 1)) throw new InvalidDataException("Timeline.EditRejected");
        var peers = events.Where(item => item.Frame == events[index].Frame).Select(item => item.Index).ToArray();
        var position = System.Array.IndexOf(peers, index); var next = position + direction;
        if (next < 0 || next >= peers.Length) return JsonSerializer.SerializeToElement(_root);
        var root = (JsonObject)_root.DeepClone(); var items = Array(root, "events"); var other = peers[next];
        var left = items[index]!.DeepClone(); var right = items[other]!.DeepClone(); items[index] = right; items[other] = left;
        return JsonSerializer.SerializeToElement(root);
    }
    public JsonElement PatchEvent(int index, IReadOnlyDictionary<string, GalNet.Game.Controls.Forms.FieldValue> patch)
    {
        if (index < 0 || index >= Events.Count) throw new InvalidDataException("Timeline.EditRejected");
        var root = (JsonObject)_root.DeepClone(); var item = (JsonObject)Array(root, "events")[index]!; var name = Name(item, "parameters");
        if (!item.ContainsKey(name)) item[name] = new JsonObject();
        var parameters = item[name] as JsonObject ?? throw new InvalidDataException("Timeline.EditRejected");
        foreach (var (key, value) in patch) { if (value.Json is { } json) parameters[key] = JsonNode.Parse(json.GetRawText()); else parameters.Remove(key); }
        return JsonSerializer.SerializeToElement(root);
    }
}
