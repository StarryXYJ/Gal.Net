using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GalNet.Core.Parameters;

public enum PropertySegmentKind { Member, Index, MapKey, JsonString }
public readonly record struct PropertySegment(PropertySegmentKind Kind, string Value);

/// <summary>Immutable wire path. Encoding boundaries distinguish a logical child from a JSON string leaf.</summary>
public sealed class PropertyPath : IReadOnlyList<PropertySegment>
{
    private readonly PropertySegment[] _segments;
    public static PropertyPath Empty { get; } = new([]);
    private PropertyPath(PropertySegment[] segments) => _segments = segments;
    public int Count => _segments.Length;
    public PropertySegment this[int index] => _segments[index];
    public PropertyPath Member(string name) => Append(new(PropertySegmentKind.Member, name));
    public PropertyPath MapKey(string key) => Append(new(PropertySegmentKind.MapKey, key));
    public PropertyPath JsonString() => Append(new(PropertySegmentKind.JsonString, ""));
    public PropertyPath Index(int index) => index < 0 ? throw new ArgumentOutOfRangeException(nameof(index))
        : Append(new(PropertySegmentKind.Index, index.ToString(CultureInfo.InvariantCulture)));
    public PropertyPath Append(PropertySegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment.Value);
        if (!Enum.IsDefined(segment.Kind) || segment.Kind == PropertySegmentKind.Index &&
            (!int.TryParse(segment.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index < 0 || segment.Value != index.ToString(CultureInfo.InvariantCulture)))
            throw new ArgumentException("Invalid property path segment.", nameof(segment));
        return new([.. _segments, segment]);
    }
    public string ToJsonPointer() => Pointer(_segments.TakeWhile(segment => segment.Kind != PropertySegmentKind.JsonString));
    public string ToLogicalPointer() => Pointer(_segments.Where(segment => segment.Kind != PropertySegmentKind.JsonString));
    private static string Pointer(IEnumerable<PropertySegment> segments) => string.Concat(segments.Select(segment => "/" + Escape(segment.Value)));
    public static string Escape(string segment) => segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    public static string Unescape(string segment) => segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);

    /// <summary>Replace an existing wire location, retaining explicit JSON-string boundaries. The input is never mutated.</summary>
    public JsonElement Replace(JsonElement source, JsonElement replacement)
    {
        if (Count > 64) throw new ArgumentException("The property path is too deep.");
        JsonNode? Apply(JsonNode? node, int depth)
        {
            if (depth == Count) return JsonNode.Parse(replacement.GetRawText());
            var segment = this[depth];
            if (segment.Kind == PropertySegmentKind.JsonString)
            {
                var decoded = JsonNode.Parse(node!.GetValue<string>(), documentOptions: new() { MaxDepth = 64 });
                return JsonValue.Create(Apply(decoded, depth + 1)?.ToJsonString() ?? "null");
            }
            if (segment.Kind == PropertySegmentKind.Index && node is JsonArray array)
            {
                var index = int.Parse(segment.Value, CultureInfo.InvariantCulture);
                if (index >= array.Count) throw new ArgumentException("The list element no longer exists.");
                var before = array[index]; var after = Apply(before, depth + 1);
                if (!ReferenceEquals(before, after)) array[index] = after;
                return array;
            }
            if (segment.Kind is PropertySegmentKind.Member or PropertySegmentKind.MapKey && node is JsonObject obj && obj.ContainsKey(segment.Value))
            {
                var before = obj[segment.Value]; var after = Apply(before, depth + 1);
                if (!ReferenceEquals(before, after)) obj[segment.Value] = after;
                return obj;
            }
            throw new ArgumentException("The wire property no longer exists.");
        }
        return JsonSerializer.SerializeToElement(Apply(JsonNode.Parse(source.GetRawText()), 0));
    }
    public IEnumerator<PropertySegment> GetEnumerator() => ((IEnumerable<PropertySegment>)_segments).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
