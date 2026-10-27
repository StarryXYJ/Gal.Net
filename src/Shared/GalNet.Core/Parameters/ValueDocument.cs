using GalNet.Core.Serialization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GalNet.Core.Parameters;

public sealed record ValueTraversalOptions(int MaximumDepth = 16, int MaximumNodes = 100_000, bool IncludeMissing = false);

/// <summary>Immutable JSON properties with caller-scoped metadata. Disposal invalidates access, not author data.</summary>
public sealed class ValueDocument : IDisposable
{
    private readonly ValueStructureResolver _resolver;
    private readonly bool _ownsResolver;
    private bool _disposed;
    internal IValueStructureProvider? Provider { get; }
    internal object? Context { get; }
    public ValueProperty Root { get; }
    public ValueDocument(FieldDescriptor root, JsonElement? value, ValueStructureResolver? resolver = null,
        IValueStructureProvider? provider = null, object? context = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        _ownsResolver = resolver is null; _resolver = resolver ?? new(); Provider = provider; Context = context;
        Root = new(this, root, value is { ValueKind: not JsonValueKind.Undefined } json ? json.Clone() : null, PropertyPath.Empty);
    }
    internal void CheckAccess() => ObjectDisposedException.ThrowIf(_disposed, this);
    internal ResolvedValueStructure Resolve(ValueProperty property) => _resolver.Resolve(property.Field, property.Value, property.Path, Provider, Context);

    public IEnumerable<ValueProperty> GetIterator(ValueTraversalOptions? options = null, CancellationToken token = default)
    {
        options ??= new();
        if (options.MaximumDepth < 0 || options.MaximumNodes < 1) throw new ArgumentOutOfRangeException(nameof(options));
        return Iterate(options, token);
    }
    private IEnumerable<ValueProperty> Iterate(ValueTraversalOptions options, CancellationToken token)
    {
        var stack = new Stack<(IEnumerator<ValueProperty> Items, int Depth)>();
        stack.Push((new[] { Root }.AsEnumerable().GetEnumerator(), 0));
        var spent = 0;
        try
        {
            while (stack.TryPeek(out var frame))
            {
                CheckAccess(); token.ThrowIfCancellationRequested();
                if (!frame.Items.MoveNext()) { stack.Pop().Items.Dispose(); continue; }
                var property = frame.Items.Current;
                if (++spent > options.MaximumNodes || frame.Depth > options.MaximumDepth)
                {
                    yield return property.WithIssue(new("analysis.limit", "Property traversal exceeded its node/depth budget."));
                    yield break;
                }
                yield return property;
                if (property.Issue is null && property.Value is not null)
                    stack.Push((property.EnumerateChildren(options.IncludeMissing).GetEnumerator(), frame.Depth + 1));
            }
        }
        finally { while (stack.TryPop(out var frame)) frame.Items.Dispose(); }
    }
    /// <summary>Resolve actual member names (and declared field IDs) against a fresh snapshot; never treats a map key as an index.</summary>
    public ValueProperty? FindProperty(IReadOnlyList<string> segments, int maximumNodes = 100_000, CancellationToken token = default)
    {
        CheckAccess();
        if (segments.Count > 64 || maximumNodes < 1) return null;
        var current = Root; var spent = 0;
        foreach (var segment in segments)
        {
            token.ThrowIfCancellationRequested();
            ValueProperty? match = null;
            foreach (var child in current.EnumerateChildren(includeMissing: true))
            {
                if (++spent > maximumNodes) return null;
                token.ThrowIfCancellationRequested();
                var comparer = current.Structure.IgnoresCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!string.Equals(child.Path[^1].Value, segment, comparer) &&
                    !(current.Structure.Kind == ValueStructureKind.Object && child.Field != current.Structure.AdditionalField
                        && string.Equals(child.Field.Id, segment, comparer))) continue;
                if (match is not null || child.Issue?.Code == "field.duplicate") return null;
                match = child;
            }
            if (match is null) return null;
            current = match;
        }
        return current;
    }
    public void Dispose() { _disposed = true; if (_ownsResolver) _resolver.Dispose(); }

    /// <summary>Opt-in nested field checks. Partial author edits do not require absent sibling fields.</summary>
    public IEnumerable<ValueIssue> Validate(bool requireAllFields = false, ValueTraversalOptions? options = null, CancellationToken token = default)
    {
        options = (options ?? new()) with { IncludeMissing = requireAllFields };
        foreach (var property in GetIterator(options, token))
        {
            if (property.Issue is { } issue) yield return issue with { Path = property.Path.ToLogicalPointer() };
            else if (requireAllFields && !property.IsProvided && property.Field.IsRequired && property.Field.DefaultValue is null)
                yield return new("field.required", "Field must be provided.", property.Path.ToLogicalPointer());
        }
    }

    /// <summary>Produce a candidate only; the caller still owns author identity, transactions and business validation.</summary>
    public ValuePatch Patch(IReadOnlyList<string> segments, JsonElement? replacement, CancellationToken token = default)
    {
        CheckAccess();
        if (segments.Count > 64) return new(null, new("analysis.limit", "The property path is too deep."));
        var budget = 100_000;
        try { return PatchCore(Root, segments, 0, replacement, ref budget, token); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or JsonException)
        { return new(null, new("structure.edit", error.Message)); }
    }
    private ValuePatch PatchCore(ValueProperty property, IReadOnlyList<string> segments, int depth, JsonElement? replacement, ref int budget, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (depth == segments.Count)
        {
            if (replacement is { } value && ValueValidation.Validate(property.Field.Value, value) is { } issue) return new(null, issue);
            if (replacement is null && property.Field.IsRequired && property.Field.DefaultValue is null) return new(null, new("field.required", "Field must be provided."));
            return new(replacement?.Clone(), null);
        }
        if (property.Issue is { } invalid) return new(null, invalid);
        if (property.Content is { ValueKind: JsonValueKind.Null }) return new(null, new("structure.obsolete", "A null object must be explicitly replaced before editing its members."));
        var structure = property.Structure;
        var comparer = structure.IgnoresCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var name = segments[depth]; ValueProperty? selected = null;
        foreach (var child in property.EnumerateChildren(includeMissing: true))
        {
            token.ThrowIfCancellationRequested();
            if (--budget < 0) return new(null, new("analysis.limit", "Property lookup exceeded its budget."));
            if (!comparer.Equals(child.Path[^1].Value, name) && !(structure.Kind == ValueStructureKind.Object
                && child.Field != structure.AdditionalField && comparer.Equals(child.Field.Id, name))) continue;
            if (selected is not null || child.Issue?.Code is "field.unknown" or "field.duplicate")
                return new(null, new("structure.ambiguous", "The property name is ambiguous or unknown."));
            selected = child;
        }
        if (selected is null && structure.Kind == ValueStructureKind.Map)
            selected = new(this, structure.Element!, null, property.Path.MapKey(name));
        if (selected is null) return new(null, new("structure.obsolete", "The object member or list element is no longer available."));
        var changed = PatchCore(selected, segments, depth + 1, replacement, ref budget, token);
        if (changed.Issue is not null) return changed;
        var node = property.Content is { } content ? JsonNode.Parse(content.GetRawText())! : new JsonObject();
        var wire = selected.Path[^1].Value;
        if (node is JsonArray array)
        {
            if (changed.Value is not { } element) return new(null, new("structure.element", "Remove an array element with a structural operation."));
            array[int.Parse(wire, System.Globalization.CultureInfo.InvariantCulture)] = JsonNode.Parse(element.GetRawText());
        }
        else if (node is JsonObject obj)
        {
            if (changed.Value is { } provided) obj[wire] = JsonNode.Parse(provided.GetRawText()); else obj.Remove(wire);
        }
        else return new(null, new("structure.obsolete", "The parent value is not an editable container."));
        return new(structure.AllowsJsonString && property.Value is { ValueKind: JsonValueKind.String }
            ? JsonCodec.SerializeToElement(node.ToJsonString()) : JsonCodec.SerializeToElement(node), null);
    }
}

public sealed record ValuePatch(JsonElement? Value, ValueIssue? Issue);

public sealed class ValueProperty
{
    private readonly ValueDocument _document;
    private readonly ValueIssue? _issue;
    private bool _decoded;
    private JsonElement? _content;
    private ValueIssue? _decodeIssue;
    private ResolvedValueStructure? _structure;
    public FieldDescriptor Field { get; }
    public JsonElement? Value { get; }
    public bool IsProvided => Value is not null;
    public PropertyPath Path { get; }
    public ResolvedValueStructure Structure { get { _document.CheckAccess(); return _structure ??= _document.Resolve(this); } }
    public ValueIssue? Issue
    {
        get
        {
            _document.CheckAccess();
            if (_issue is not null) return _issue;
            var content = Content;
            if (_decodeIssue is not null) return _decodeIssue;
            if (content is null) return null;
            if (content.Value.ValueKind == JsonValueKind.Null)
                return Field.Value.AllowNull ? null : new("value.null", "Value must not be null.");
            if (Structure.Issue is { } issue)
                return content.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object || issue.Code != "field.opaque"
                    || Field.Value.ValueType != typeof(JsonElement) || Field.Value.Structure?.ResolverId is not null ? issue : null;
            var expected = Structure.Kind switch { ValueStructureKind.Object or ValueStructureKind.Map => JsonValueKind.Object, ValueStructureKind.Sequence => JsonValueKind.Array, _ => JsonValueKind.Undefined };
            if (expected != JsonValueKind.Undefined && content.Value.ValueKind != expected)
                return new(expected == JsonValueKind.Array ? "field.arrayRequired" : "field.objectRequired", expected == JsonValueKind.Array ? "Expected a list." : "Expected an object.");
            if (Field.Semantic != FieldValueSemantic.None && content.Value.ValueKind != JsonValueKind.String)
                return new("field.stringRequired", "Localized fields must contain string keys.");
            if (Structure.Kind == ValueStructureKind.Scalar && Field.Semantic == FieldValueSemantic.None)
                return ValueValidation.Validate(Field.Value, content.Value);
            return null;
        }
    }
    internal ValueProperty(ValueDocument document, FieldDescriptor field, JsonElement? value, PropertyPath path, ValueIssue? issue = null)
    { _document = document; Field = field; Value = value; Path = path; _issue = issue; }
    internal ValueProperty WithIssue(ValueIssue issue) => new(_document, Field, Value, Path, issue);
    public JsonElement? Content
    {
        get
        {
            _document.CheckAccess();
            if (_decoded) return _content;
            _decoded = true; _content = Value;
            if (Structure.AllowsJsonString && Value is { ValueKind: JsonValueKind.String } encoded)
            {
                try { using var json = JsonDocument.Parse(encoded.GetString()!, new() { MaxDepth = 64 }); _content = json.RootElement.Clone(); }
                catch (JsonException error) { _decodeIssue = new("field.objectRequired", error.Message); }
            }
            return _content;
        }
    }
    public IEnumerable<ValueProperty> EnumerateChildren(bool includeMissing = false)
    {
        _document.CheckAccess();
        if (Issue is not null || Content is { ValueKind: JsonValueKind.Null }) yield break;
        var content = Content;
        var path = Structure.AllowsJsonString && Value is { ValueKind: JsonValueKind.String } ? Path.JsonString() : Path;
        if (Structure.Kind == ValueStructureKind.Object)
        {
            var comparer = Structure.IgnoresCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var seen = new HashSet<string>(comparer);
            if (content is { ValueKind: JsonValueKind.Object } obj)
                foreach (var property in obj.EnumerateObject())
                {
                    var member = Structure.Members.FirstOrDefault(member => comparer.Equals(member.WireName, property.Name));
                    var field = member?.Field ?? Structure.AdditionalField;
                    var issue = !seen.Add(property.Name) ? new ValueIssue("field.duplicate", "Duplicate author property.")
                        : field is null ? new ValueIssue("field.unknown", "Unknown author field cannot be analyzed.") : null;
                    yield return new(_document, field ?? UnknownField, property.Value, path.Member(property.Name), issue);
                }
            if (includeMissing)
                foreach (var member in Structure.Members)
                    if (!seen.Contains(member.WireName)) yield return new(_document, member.Field, null, path.Member(member.WireName));
        }
        else if (Structure.Kind == ValueStructureKind.Sequence && content is { ValueKind: JsonValueKind.Array } array)
        {
            var index = 0;
            foreach (var item in array.EnumerateArray()) yield return new(_document, Structure.Element!, item, path.Index(index++));
        }
        else if (Structure.Kind == ValueStructureKind.Map && content is { ValueKind: JsonValueKind.Object } map)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in map.EnumerateObject())
                yield return new(_document, Structure.Element!, member.Value, path.MapKey(member.Name), seen.Add(member.Name) ? null : new("field.duplicate", "Duplicate map key."));
        }
    }
    private static FieldDescriptor UnknownField { get; } = new("unknown", new(typeof(JsonElement)));
}
