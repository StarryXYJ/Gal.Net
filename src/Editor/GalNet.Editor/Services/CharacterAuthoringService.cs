using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

public sealed record CharacterRow(string Id, string DisplayNameKey, string? Name, int Uses)
{
    public string DisplayName => Name ?? Id;
    public override string ToString() => DisplayName + " · " + Id;
}

/// <summary>Character identities, default-language names and declared references share one project history.</summary>
public sealed class CharacterAuthoringService(LocalizationDocumentStore store, ProjectLocalizationQuery query,
    IProjectContentSession content, IEditorDocumentService documents, IEntryCatalog catalog,
    LocalizationAuthoringService authoring, IEditorLocalizationService text, GameTextAuthoringService texts)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public IEditorLocalizationService Text => text;
    public event Action? Changed { add => content.Changed += value; remove => content.Changed -= value; }
    private LocalizationReferenceIndex Index => query.Get() ?? throw Error("character.obsolete");
    public bool Complete => query.Get()?.Complete == true;
    public IReadOnlyList<CharacterRow> Rows()
    {
        var index = Index;
        var culture = content.CaptureWorking().Settings.TargetLocale.Code;
        var counts = index.References.Where(reference => reference.Semantic == FieldValueSemantic.CharacterReference)
            .GroupBy(reference => reference.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return index.Characters.Select(character => new CharacterRow(character.Id, character.DisplayNameKey,
            index.FindDirect(culture, character.DisplayNameKey)?.Text,
            counts.GetValueOrDefault(character.Id))).ToArray();
    }
    private EditorOperationException Error(string code) => new(code, text["Error." + code]);
    private static JsonArray ReadCatalog(LocalizationFileSnapshot snapshot)
    {
        LocalizationJson.Validate(snapshot.Path, snapshot.Data);
        return JsonNode.Parse(LocalizationJson.Decode(snapshot.Data))!.AsArray();
    }
    private static JsonObject Find(JsonArray array, string id) => array.Select(node => node!.AsObject()).Single(node => node["id"]!.GetValue<string>() == id);
    private async Task EnsureFilesAsync()
    {
        var locale = content.CaptureWorking().Settings.TargetLocale.Code;
        var files = new[] { store.CaptureFile(LocalizationJson.CatalogPath), store.CaptureFile($"I18n/{locale}/characters.json") };
        var missing = files.Where(file => !file.Exists).Select(file => new LocalizationFilePatch(file, file.Data)).ToArray();
        if (missing.Length > 0) await store.Prepare(missing).ApplyAsync(true);
    }
    private void ValidateName(string name) { if (string.IsNullOrWhiteSpace(name)) throw Error("character.name"); }
    private void RequireComplete() { if (!Complete) throw Error("character.incomplete"); }

    public async Task<CharacterRow> CreateAsync(string name, string? key = null)
    {
        await _gate.WaitAsync();
        try { return await CreateCoreAsync(name, key); }
        finally { _gate.Release(); }
    }
    private async Task<CharacterRow> CreateCoreAsync(string name, string? key)
    {
        ValidateName(name);
        if (key is not null && string.IsNullOrWhiteSpace(key)) throw Error("character.key");
        if (key is not null && Rows().Any(row => row.Id == key)) throw Error("character.duplicate");
        await EnsureFilesAsync();
        key ??= await store.ReserveCharacterKeyAsync();
        var file = store.CaptureFile(LocalizationJson.CatalogPath);
        await content.CommitFileDraftsAsync(file.Id);
        var array = ReadCatalog(file);
        if (array.Any(node => node!["id"]!.GetValue<string>() == key)) throw Error("character.duplicate");
        var nameKey = "character." + Guid.NewGuid().ToString("N") + ".name";
        array.Add(new JsonObject { ["id"] = key, ["displayNameKey"] = nameKey });
        var target = store.CaptureText(nameKey, newFile: $"I18n/{content.CaptureWorking().Settings.TargetLocale.Code}/characters.json");
        await content.CommitFileDraftsAsync(target.File.Id);
        var patches = new[] { new LocalizationFilePatch(file, LocalizationJson.WriteLike(file.Data, array)), store.PrepareText(target, name) };
        await authoring.ApplyAsync(store.Prepare(patches), key, "History.Character");
        return Rows().Single(row => row.Id == key);
    }

    public async Task UpdateAsync(CharacterRow expected, string key, string name)
    {
        await _gate.WaitAsync();
        try
        {
            ValidateName(name);
            if (string.IsNullOrWhiteSpace(key)) throw Error("character.key");
            if (Rows().Any(row => row.Id == key && row.Id != expected.Id)) throw Error("character.duplicate");
            if (key != expected.Id) RequireComplete();
            var before = store.CaptureFile(LocalizationJson.CatalogPath);
            var array = ReadCatalog(before);
            var row = Rows().SingleOrDefault(row => row.Id == expected.Id);
            if (row is null || row.DisplayNameKey != expected.DisplayNameKey || row.Name != expected.Name) throw Error("character.changed");
            if (key == row.Id && name == row.Name) return;
            var target = store.CaptureText(row.DisplayNameKey);
            await content.CommitFileDraftsAsync(target.File.Id);
            Find(array, expected.Id)["id"] = key;
            var refs = key != expected.Id ? ReferencePatches(expected.Id, key) : [];
            var patches = await texts.PrepareTextAsync(target, name, [new(before, LocalizationJson.WriteLike(before.Data, array))], () =>
            { if (Rows().SingleOrDefault(row => row.Id == expected.Id)?.Name != expected.Name) throw Error("character.changed"); });
            await authoring.ApplyAsync(store.PrepareWithEntries(
                patches, refs), key, "History.Character");
        }
        finally { _gate.Release(); }
    }
    public async Task DeleteAsync(CharacterRow expected)
    {
        await _gate.WaitAsync();
        try
        {
            RequireComplete();
            var row = Rows().SingleOrDefault(row => row.Id == expected.Id);
            if (row is null || row.DisplayNameKey != expected.DisplayNameKey || row.Name != expected.Name) throw Error("character.changed");
            if (row.Uses > 0) throw Error("character.inUse");
            var file = store.CaptureFile(LocalizationJson.CatalogPath);
            var array = ReadCatalog(file); array.Remove(Find(array, row.Id));
            await authoring.ApplyAsync(store.Prepare(new LocalizationFilePatch(file, LocalizationJson.WriteLike(file.Data, array))), row.Id, "History.Character");
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> CommitParametersAsync(string groupId, EditorEntryData expected, string type,
        Dictionary<string, JsonElement> updated, Func<bool> isCurrent, CancellationToken cancellationToken)
    {
        var before = expected.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
        var candidate = new EditorEntryData { StableId = expected.StableId, Type = type, Parameters = updated };
        var oldRefs = References(groupId, expected).ToDictionary(reference => reference.JsonPointer, reference => reference.Key, StringComparer.Ordinal);
        var changed = References(groupId, candidate).Where(reference => oldRefs.GetValueOrDefault(reference.JsonPointer) != reference.Key).ToArray();
        if (changed.Length == 0) return false;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            void Current()
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!isCurrent() || expected.Type != type || !LocalizationEditOperation.Same(expected.Parameters, before)) throw Error("character.obsolete");
            }
            Current();
            var root = JsonSerializer.SerializeToNode(updated)!.AsObject();
            // Resolve against the catalog after admission to the gate, never an old name-search snapshot.
            var rows = Rows(); var needed = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var reference in changed)
            {
                var exact = rows.SingleOrDefault(row => row.Id == reference.Key);
                var names = rows.Where(row => row.Name == reference.Key).ToArray();
                if (exact is null && names.Length > 1) throw Error("character.ambiguous");
                if (exact is not null || names.Length == 1) Set(root, reference.JsonPointer, (exact ?? names[0]).Id);
                else needed.TryAdd(reference.Key, "");
            }
            var filePatches = new List<LocalizationFilePatch>();
            if (needed.Count > 0)
            {
                await EnsureFilesAsync();
                foreach (var name in needed.Keys.ToArray()) { needed[name] = await store.ReserveCharacterKeyAsync(); Current(); }
                var file = store.CaptureFile(LocalizationJson.CatalogPath); var array = ReadCatalog(file);
                var nameFile = store.CaptureFile($"I18n/{content.CaptureWorking().Settings.TargetLocale.Code}/characters.json");
                await content.CommitFileDraftsAsync(nameFile.Id); Current(); nameFile = store.CaptureFile(nameFile.Path);
                var namesBytes = nameFile.Data;
                foreach (var (name, id) in needed)
                {
                    var nameKey = "character." + Guid.NewGuid().ToString("N") + ".name";
                    array.Add(new JsonObject { ["id"] = id, ["displayNameKey"] = nameKey });
                    namesBytes = LocalizationJson.SetText(namesBytes, nameKey, name);
                }
                filePatches.Add(new(file, LocalizationJson.WriteLike(file.Data, array))); filePatches.Add(new(nameFile, namesBytes));
                foreach (var reference in changed.Where(reference => needed.ContainsKey(reference.Key))) Set(root, reference.JsonPointer, needed[reference.Key]);
            }
            Current();
            var after = root.Deserialize<Dictionary<string, JsonElement>>()!;
            if (await texts.PrepareEntryAsync(groupId, expected, after, filePatches, Current) is { } localized)
            { after = localized.Parameters; filePatches = localized.Files.ToList(); }
            Current();
            ValidateParameters(type, after, before);
            await authoring.ApplyAsync(store.PrepareWithEntries(filePatches, [new(groupId, expected.StableId, type, before, after)]), expected.StableId, "History.Character");
            return true;
        }
        finally { _gate.Release(); }
    }

    private IReadOnlyList<LocalizationReference> References(string group, EditorEntryData entry) => new LocalizationReferenceIndex(
        new() { GroupEntries = new() { [group] = [entry] } }, catalog, []).References.Where(reference => reference.Semantic == FieldValueSemantic.CharacterReference).ToArray();
    private IReadOnlyList<LocalizationEntryPatch> ReferencePatches(string from, string to)
    {
        var patches = new List<LocalizationEntryPatch>();
        foreach (var group in Index.References.Where(reference => reference.Semantic == FieldValueSemantic.CharacterReference && reference.Key == from)
                     .GroupBy(reference => (reference.Owner, reference.EntryId)))
        {
            var entry = documents.Groups[group.Key.Owner].Entries.Single(entry => entry.StableId == group.Key.EntryId);
            var node = JsonSerializer.SerializeToNode(entry.Parameters)!.AsObject();
            foreach (var reference in group) Set(node, reference.JsonPointer, to);
            var after = node.Deserialize<Dictionary<string, JsonElement>>()!;
            ValidateParameters(entry.Type, after, entry.Parameters);
            patches.Add(new(group.Key.Owner, entry.StableId, entry.Type, entry.Parameters, after));
        }
        return patches;
    }
    private void ValidateParameters(string type, IReadOnlyDictionary<string, JsonElement> values, IReadOnlyDictionary<string, JsonElement> before)
    {
        var schema = catalog.Get(type).DynamicParameters;
        schema?.ValidatePartialArguments(JsonSerializer.SerializeToElement(values.Where(pair => schema.ContainsKey(pair.Key) &&
            (!before.TryGetValue(pair.Key, out var previous) || !JsonElement.DeepEquals(previous, pair.Value))).ToDictionary(pair => pair.Key, pair => pair.Value)));
    }
    private static void Set(JsonNode root, string pointer, string value)
    {
        var parts = pointer.Split('/').Skip(1).Select(part => part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)).ToArray();
        var parent = root;
        foreach (var part in parts.SkipLast(1)) parent = parent is JsonArray array ? array[int.Parse(part, System.Globalization.CultureInfo.InvariantCulture)]! : parent[part]!;
        if (parent is JsonArray list) list[int.Parse(parts[^1], System.Globalization.CultureInfo.InvariantCulture)] = value;
        else parent[parts[^1]] = value;
    }
}
