using System.Globalization;
using System.Reflection;
using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Services;

public sealed record LocalizationSource(string Path, string Json);
public sealed record LocalizationReference(string Owner, string? EntryId, string JsonPointer, string Key, FieldValueSemantic Semantic);
public sealed record LocalizationText(string Culture, string Key, string Text, string File, string JsonPointer);
public sealed record LocalizationIssue(string Code, string File, string JsonPointer, string Message);
public sealed record LocalizationCharacter(string Id, string DisplayNameKey);

/// <summary>Read-only, bounded analysis of one author snapshot. Never executes entries or changes keys.</summary>
public sealed class LocalizationReferenceIndex
{
    private const int MaximumDepth = 16;
    private const int MaximumValues = 100_000;
    private readonly List<LocalizationReference> _references = [];
    private readonly List<LocalizationText> _texts = [];
    private readonly List<LocalizationIssue> _issues = [];
    private readonly List<LocalizationCharacter> _characters = [];
    private readonly Dictionary<string, Dictionary<string, LocalizationText>> _effective = new(StringComparer.OrdinalIgnoreCase);
    private int _remaining = MaximumValues;

    public LocalizationReferenceIndex(EditorProjectDocument document, IEntryCatalog catalog, IEnumerable<LocalizationSource> sources,
        IEnumerable<LocalizationIssue>? sourceIssues = null)
    {
        foreach (var issue in sourceIssues ?? []) Issue(issue.Code, issue.File, issue.JsonPointer, issue.Message);
        foreach (var source in sources.OrderBy(source => source.Path, StringComparer.Ordinal)) ReadSource(source);
        foreach (var node in document.Graph.Nodes.Where(node => node.Type == "Group"))
            if (!document.GroupEntries.ContainsKey(node.Id)) Issue("group.missing", node.Id, "", "Author Group is unavailable.");
        foreach (var (groupId, entries) in document.GroupEntries)
        {
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (!Spend(groupId, "", 0)) break;
                if (string.IsNullOrWhiteSpace(entry.StableId) || !identities.Add(entry.StableId))
                    Issue("entry.identity", groupId, "", "Entry identity is missing or duplicated.");
                if (!catalog.TryGet(entry.Type, out var definition) || definition.DynamicParameters is null)
                { Issue("schema.unavailable", groupId, "", $"No field schema for '{entry.Type}'."); continue; }
                foreach (var name in entry.Parameters.Keys.Where(name => !definition.DynamicParameters.ContainsKey(name)))
                    Issue("field.unknown", groupId, "/" + Escape(name), "Unknown author field cannot be analyzed.");
                foreach (var field in definition.DynamicParameters.Fields)
                    if (entry.Parameters.TryGetValue(field.Id, out var value))
                        Walk(field, value, groupId, entry.StableId, "/" + Escape(field.Id), 0);
            }
        }
        foreach (var node in document.Graph.Nodes.Where(node => node.Type == "Branch" && node.BranchType == "Choice"))
            foreach (var option in node.Options ?? [])
            {
                if (!Spend(node.Id, "", 0)) break;
                Reference(node.Id, option.Id, "/text", option.Text, FieldValueSemantic.LocalizedText);
            }
        var ids = _characters.Select(character => character.Id).ToHashSet(StringComparer.Ordinal);
        var defaultKeys = _texts.Where(text => text.Culture.Equals(document.Settings.TargetLocale.Code, StringComparison.OrdinalIgnoreCase))
            .Select(text => text.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var reference in _references)
            if (reference.Semantic == FieldValueSemantic.CharacterReference ? !ids.Contains(reference.Key) : !defaultKeys.Contains(reference.Key))
                Issue(reference.Semantic == FieldValueSemantic.CharacterReference ? "character.unknown" : "text.defaultMissing",
                    reference.Owner, reference.JsonPointer, $"Unresolved reference '{reference.Key}'.", incomplete: false);
        References = _references.AsReadOnly(); Texts = _texts.AsReadOnly(); Issues = _issues.AsReadOnly(); Characters = _characters.AsReadOnly();
    }

    public IReadOnlyList<LocalizationReference> References { get; }
    /// <summary>All definitions in load order, including shadowed definitions.</summary>
    public IReadOnlyList<LocalizationText> Texts { get; }
    public IReadOnlyList<LocalizationIssue> Issues { get; }
    public IReadOnlyList<LocalizationCharacter> Characters { get; }
    /// <summary>False means absence of a reference cannot establish that a character is unused.</summary>
    public bool Complete { get; private set; } = true;
    public LocalizationText? FindDirect(string culture, string key) => _effective.GetValueOrDefault(culture)?.GetValueOrDefault(key);

    private void Walk(FieldDescriptor field, JsonElement value, string owner, string entryId, string pointer, int depth, JsonSerializerOptions? wireOptions = null)
    {
        if (!Spend(owner, pointer, depth) || value.ValueKind == JsonValueKind.Null) return;
        if (field.Semantic != FieldValueSemantic.None)
        {
            if (value.ValueKind != JsonValueKind.String) Issue("field.stringRequired", owner, pointer, "Localized fields must contain string keys.");
            else Reference(owner, entryId, pointer, value.GetString()!, field.Semantic);
            return;
        }
        WalkValue(field.Value.ValueType, value, owner, entryId, pointer, depth, wireOptions ?? field.Value.JsonOptions);
    }

    private void WalkValue(Type type, JsonElement value, string owner, string entryId, string pointer, int depth, JsonSerializerOptions options)
    {
        if (value.ValueKind == JsonValueKind.Null) return;
        var elementType = type.IsArray && type.GetArrayRank() == 1 ? type.GetElementType()
            : type.IsGenericType && type.GetGenericTypeDefinition() is var generic &&
              (generic == typeof(List<>) || generic == typeof(IList<>) || generic == typeof(IReadOnlyList<>) || generic == typeof(IEnumerable<>))
                ? type.GetGenericArguments()[0] : null;
        if (elementType is not null)
        {
            if (value.ValueKind != JsonValueKind.Array) { Issue("field.arrayRequired", owner, pointer, "Expected a list."); return; }
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                var child = pointer + "/" + index++;
                if (!Spend(owner, child, depth + 1)) break;
                WalkValue(elementType, item, owner, entryId, child, depth + 1, options);
            }
        }
        else if (typeof(IFieldDto).IsAssignableFrom(type))
        {
            if (value.ValueKind != JsonValueKind.Object) { Issue("field.objectRequired", owner, pointer, "Expected an object."); return; }
            IReadOnlyList<FieldDescriptor>? fields;
            try { fields = type.GetProperty("Schema", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null) as IReadOnlyList<FieldDescriptor>; }
            catch (Exception error) when (error is TargetInvocationException or TypeInitializationException)
            { Issue("schema.invalid", owner, pointer, error.Message); return; }
            if (fields is null) { Issue("schema.unavailable", owner, pointer, "Nested DTO has no readable schema."); return; }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
                if (!names.Add(property.Name)) Issue("field.duplicate", owner, pointer, "Duplicate author property.");
            var properties = options.GetTypeInfo(type).Properties;
            var mapped = fields.Select(field =>
            {
                var member = type.GetMembers(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(property => property is PropertyInfo or FieldInfo && property.GetCustomAttribute<FieldAttribute>() is not null &&
                    (property.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name)) == field.Id);
                var wire = properties.FirstOrDefault(property => property.AttributeProvider is MemberInfo info && info.Name == member?.Name)?.Name ?? field.Id;
                return (Field: field, Wire: wire);
            }).ToArray();
            foreach (var member in mapped)
                if (value.TryGetProperty(member.Wire, out var child)) Walk(member.Field, child, owner, entryId, pointer + "/" + Escape(member.Wire), depth + 1, options);
            foreach (var name in names.Where(name => !mapped.Any(member => member.Wire == name)))
                Issue("field.unknown", owner, pointer + "/" + Escape(name), "Unknown nested author field cannot be analyzed.");
        }
        else if (value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            Issue("field.opaque", owner, pointer, "Structured value has no traversable field schema.");
    }

    private void Reference(string owner, string? entryId, string pointer, string key, FieldValueSemantic semantic)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            if (semantic == FieldValueSemantic.LocalizedText) Issue("text.emptyKey", owner, pointer, "Text key is empty.", incomplete: false);
            return;
        }
        _references.Add(new(owner, entryId, pointer, key, semantic));
    }

    private void ReadSource(LocalizationSource source)
    {
        var path = source.Path.Replace('\\', '/');
        if (!path.StartsWith("I18n/", StringComparison.Ordinal)) return;
        var parts = path.Split('/');
        if (parts.Length < 3) { Issue("source.layout", path, "", "Localization files must be below a culture directory."); return; }
        if (parts[1].Equals("External", StringComparison.OrdinalIgnoreCase)) return;
        if (parts[1].Equals("catalog", StringComparison.OrdinalIgnoreCase) && path != "I18n/catalog/characters.json") return;
        try
        {
            using var json = JsonDocument.Parse(source.Json);
            if (path == "I18n/catalog/characters.json")
            {
                if (json.RootElement.ValueKind != JsonValueKind.Array) { Issue("catalog.invalid", path, "", "Expected character array."); return; }
                var ids = _characters.Select(character => character.Id).ToHashSet(StringComparer.Ordinal);
                var index = 0;
                foreach (var element in json.RootElement.EnumerateArray())
                {
                    var pointer = "/" + index++;
                    if (!Spend(path, pointer, 0)) break;
                    var id = ProjectRawReader.Text(element, "id"); var name = ProjectRawReader.Text(element, "displayNameKey");
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || !ids.Add(id))
                    { Issue("catalog.invalid", path, pointer, "Missing name key or duplicate/empty character ID."); continue; }
                    if (element.EnumerateObject().GroupBy(property => property.Name, StringComparer.Ordinal).Any(group => group.Count() > 1))
                        Issue("catalog.duplicateProperty", path, pointer, "Duplicate catalog property.");
                    _characters.Add(new(id, name));
                    Reference("characters", id, pointer + "/displayNameKey", name, FieldValueSemantic.LocalizedText);
                }
            }
            else
            {
                var culture = CultureInfo.GetCultureInfo(parts[1]).Name;
                if (culture.Length == 0) { Issue("culture.invalid", path, "", "Invariant culture is not a project language."); return; }
                Flatten(json.RootElement, "", "", path, culture, new(StringComparer.Ordinal), 0);
            }
        }
        catch (Exception error) when (error is JsonException or CultureNotFoundException)
        { Issue("source.invalid", path, "", error.Message); }
    }

    private void Flatten(JsonElement element, string prefix, string pointer, string path, string culture, HashSet<string> keys, int depth)
    {
        if (!Spend(path, pointer, depth)) return;
        if (element.ValueKind != JsonValueKind.Object) { Issue("text.objectRequired", path, pointer, "Expected an object with string leaves."); return; }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            var child = pointer + "/" + Escape(property.Name);
            if (!Spend(path, child, depth + 1)) break;
            if (!names.Add(property.Name) || string.IsNullOrWhiteSpace(property.Name))
                Issue("text.duplicateProperty", path, child, "Duplicate or empty property name.");
            var key = prefix.Length == 0 ? property.Name : prefix + "." + property.Name;
            if (property.Value.ValueKind == JsonValueKind.Object) Flatten(property.Value, key, child, path, culture, keys, depth + 1);
            else if (property.Value.ValueKind == JsonValueKind.String)
            {
                if (!keys.Add(key)) Issue("text.keyCollision", path, child, $"Key '{key}' has multiple definitions in one file.");
                var text = new LocalizationText(culture, key, property.Value.GetString()!, path, child);
                _texts.Add(text);
                if (!_effective.TryGetValue(culture, out var values)) _effective[culture] = values = new(StringComparer.Ordinal);
                values[key] = text;
            }
            else Issue("text.stringRequired", path, child, "Translation must be a string.");
        }
    }

    private bool Spend(string file, string pointer, int depth)
    {
        if (_remaining <= 0) return false;
        if (--_remaining == 0 || depth > MaximumDepth)
        { Issue("analysis.limit", file, pointer, "Localization analysis exceeded its value/depth budget."); return false; }
        return true;
    }
    private void Issue(string code, string file, string pointer, string message, bool incomplete = true)
    {
        if (incomplete) Complete = false;
        _issues.Add(new(code, file, pointer, message));
    }
    private static string Escape(string name) => name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
