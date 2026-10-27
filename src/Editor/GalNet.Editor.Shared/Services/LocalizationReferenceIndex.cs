using System.Globalization;
using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Services;

public sealed record LocalizationSource(string Path, string Json);
public sealed record LocalizationReference(string Owner, string? EntryId, string JsonPointer, string Key, FieldValueSemantic Semantic)
{
    public PropertyPath? Path { get; init; }
    public string LocationKey => Path?.ToLogicalPointer() ?? JsonPointer;
    public JsonElement Rewrite(JsonElement parameters, string key)
    {
        var path = Path;
        if (path is null)
        {
            path = PropertyPath.Empty; var current = parameters;
            foreach (var segment in JsonPointer.Split('/').Skip(1).Select(PropertyPath.Unescape))
            {
                if (current.ValueKind == JsonValueKind.Array)
                { var index = int.Parse(segment, CultureInfo.InvariantCulture); path = path.Index(index); current = current[index]; }
                else { path = path.Member(segment); current = current.GetProperty(segment); }
            }
        }
        return path.Replace(parameters, JsonSerializer.SerializeToElement(key));
    }
}
public sealed record LocalizationText(string Culture, string Key, string Text, string File, string JsonPointer);
public sealed record LocalizationIssue(string Code, string File, string JsonPointer, string Message)
{
    public string? EntryId { get; init; }
    public PropertyPath? Path { get; init; }
}
public sealed record LocalizationCharacter(string Id, string DisplayNameKey);

/// <summary>Read-only, bounded analysis of one author snapshot. Never executes entries or changes keys.</summary>
public sealed class LocalizationReferenceIndex
{
    private const int MaximumDepth = 16;
    private const int MaximumValues = 100_000;
    private readonly List<LocalizationReference> _references = [];
    private readonly List<LocalizationText> _texts = [];
    private readonly List<LocalizationIssue> _issues = [];
    private readonly List<LocalizationIssue> _incompleteIssues = [];
    private readonly List<LocalizationCharacter> _characters = [];
    private readonly Dictionary<string, Dictionary<string, LocalizationText>> _effective = new(StringComparer.OrdinalIgnoreCase);
    private int _remaining = MaximumValues;

    public LocalizationReferenceIndex(EditorProjectDocument document, IEntryCatalog catalog, IEnumerable<LocalizationSource> sources,
        IEnumerable<LocalizationIssue>? sourceIssues = null)
        : this(document, catalog, sources, sourceIssues, new()) { }

    public LocalizationReferenceIndex(EditorProjectDocument document, IEntryCatalog catalog, IEnumerable<LocalizationSource> sources,
        IEnumerable<LocalizationIssue>? sourceIssues, EntryParameterMetadata metadata)
    {
        using var structures = new ValueStructureResolver();
        var parameters = new EntryParameterStructures(document, metadata, catalog);
        foreach (var issue in sourceIssues ?? []) Issue(issue.Code, issue.File, issue.JsonPointer, issue.Message);
        foreach (var source in sources.OrderBy(source => source.Path, StringComparer.Ordinal)) ReadSource(source);
        foreach (var node in document.Graph.Nodes.Where(node => node.Type == "Group"))
            if (!document.GroupEntries.ContainsKey(node.Id)) Issue("group.missing", node.Id, "", "Author Group is unavailable.");
        foreach (var (groupId, entries) in document.GroupEntries)
        {
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (!Spend(groupId, "", 0, entry.StableId)) break;
                if (string.IsNullOrWhiteSpace(entry.StableId) || !identities.Add(entry.StableId))
                    Issue("entry.identity", groupId, "", "Entry identity is missing or duplicated.", entryId: entry.StableId);
                if (!catalog.TryGet(entry.Type, out var definition))
                {
                    if (entry.Parameters.Values.Any(value => !IsEmpty(value)))
                        Issue("schema.unavailable", groupId, "", $"No field schema for '{entry.Type}'.", entryId: entry.StableId);
                    continue;
                }
                foreach (var name in entry.Parameters.Keys.Where(name => !definition.DynamicParameters.ContainsKey(name) && !IsEmpty(entry.Parameters[name])))
                    Issue("field.unknown", groupId, "/" + Escape(name), "Unknown author field cannot be analyzed.", entryId: entry.StableId);
                foreach (var field in definition.DynamicParameters.Fields)
                    if (entry.Parameters.TryGetValue(field.Id, out var value))
                        Walk(parameters.Describe(field, groupId, entry), value, groupId, entry.StableId, "/" + Escape(field.Id), structures, parameters, new EntryStructureContext(groupId, entry));
            }
        }
        foreach (var node in document.Graph.Nodes.Where(node => node.Type == "Branch" && node.BranchType == "Choice"))
            foreach (var option in node.Options ?? [])
            {
                if (!Spend(node.Id, "", 0)) break;
                Reference(node.Id, option.Id, "/text", option.Text, FieldValueSemantic.LocalizedText);
            }
        var ids = _characters.Select(character => character.Id).ToHashSet(StringComparer.Ordinal);
        var defaultCulture = document.Configuration.ReadDefaultOptions().TargetLocale.Code;
        var defaultKeys = _texts.Where(text => text.Culture.Equals(defaultCulture, StringComparison.OrdinalIgnoreCase))
            .Select(text => text.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var reference in _references)
            if (reference.Semantic == FieldValueSemantic.CharacterReference ? !ids.Contains(reference.Key) : !defaultKeys.Contains(reference.Key))
                Issue(reference.Semantic == FieldValueSemantic.CharacterReference ? "character.unknown" : "text.defaultMissing",
                    reference.Owner, reference.JsonPointer, $"Unresolved reference '{reference.Key}'.", incomplete: false, entryId: reference.EntryId, path: reference.Path);
        References = _references.AsReadOnly(); Texts = _texts.AsReadOnly(); Issues = _issues.AsReadOnly();
        IncompleteIssues = _incompleteIssues.AsReadOnly(); Characters = _characters.AsReadOnly();
    }

    public IReadOnlyList<LocalizationReference> References { get; }
    /// <summary>All definitions in load order, including shadowed definitions.</summary>
    public IReadOnlyList<LocalizationText> Texts { get; }
    public IReadOnlyList<LocalizationIssue> Issues { get; }
    /// <summary>Issues that make it unsafe to treat an absent reference as proof of non-use.</summary>
    public IReadOnlyList<LocalizationIssue> IncompleteIssues { get; }
    public IReadOnlyList<LocalizationCharacter> Characters { get; }
    /// <summary>False means absence of a reference cannot establish that a character is unused.</summary>
    public bool Complete { get; private set; } = true;
    public LocalizationText? FindDirect(string culture, string key) => _effective.GetValueOrDefault(culture)?.GetValueOrDefault(key);

    private void Walk(FieldDescriptor field, JsonElement value, string owner, string entryId, string pointer, ValueStructureResolver structures,
        IValueStructureProvider provider, object context)
    {
        if (IsEmpty(value)) return;
        using var document = new ValueDocument(field, value, structures, provider, context);
        foreach (var property in document.GetIterator(new(MaximumDepth, Math.Max(1, _remaining))))
        {
            var logical = PropertyPath.Empty.Member(field.Id);
            foreach (var segment in property.Path) logical = logical.Append(segment);
            var location = pointer + property.Path.ToJsonPointer();
            if (!Spend(owner, location, property.Path.Count, entryId)) break;
            if (property.Value is not { } provided || IsEmpty(provided)) continue;
            // Root scalar validation is already owned by the field contract; this index adds nested coverage.
            if (property.Path.Count == 0 && property.Issue?.Code.StartsWith("value.", StringComparison.Ordinal) == true) continue;
            if (property.Issue is { } issue)
                Issue(issue.Code, owner, location, issue.Message, entryId: entryId, path: logical);
            else if (property.Field.Semantic != FieldValueSemantic.None)
            {
                if (provided.ValueKind != JsonValueKind.String)
                    Issue("field.stringRequired", owner, location, "Localized fields must contain string keys.", entryId: entryId, path: logical);
                else Reference(owner, entryId, location, provided.GetString()!, property.Field.Semantic, logical);
            }
        }
    }

    private void Reference(string owner, string? entryId, string pointer, string key, FieldValueSemantic semantic, PropertyPath? path = null)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        _references.Add(new(owner, entryId, pointer, key, semantic) { Path = path });
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

    private bool Spend(string file, string pointer, int depth, string? entryId = null)
    {
        if (_remaining <= 0) return false;
        if (--_remaining <= 0 || depth > MaximumDepth)
        { Issue("analysis.limit", file, pointer, "Localization analysis exceeded its value/depth budget.", entryId: entryId); return false; }
        return true;
    }
    private void Issue(string code, string file, string pointer, string message, bool incomplete = true, string? entryId = null, PropertyPath? path = null)
    {
        var issue = new LocalizationIssue(code, file, pointer, message) { EntryId = entryId, Path = path };
        _issues.Add(issue);
        if (!incomplete) return;
        Complete = false;
        _incompleteIssues.Add(issue);
    }
    private static bool IsEmpty(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => true,
        JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()),
        JsonValueKind.Object => !value.EnumerateObject().Any(),
        JsonValueKind.Array => value.GetArrayLength() == 0,
        _ => false
    };

    private static string Escape(string name) => name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
