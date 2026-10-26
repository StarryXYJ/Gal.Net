using GalNet.Contracts.Builtins;
using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Shared.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Services;

public sealed record GameTextDraft(string Token, string Key, LocalizationTextTarget Target, string Text,
    IReadOnlyDictionary<string, string>? Translations = null, JsonElement? State = null, bool WriteSource = true);
public sealed record GameTextClipboard(Dictionary<string, Dictionary<string, string>> Texts, Dictionary<string, JsonElement> States);
public sealed record GameTextChange(Dictionary<string, JsonElement> Parameters, IReadOnlyList<LocalizationFilePatch> Files);

/// <summary>Semantic text intents are resolved only at the document commit boundary.</summary>
public sealed class GameTextAuthoringService(LocalizationDocumentStore store, ProjectLocalizationQuery query,
    IProjectContentSession content, IEditorDocumentService documents, IEntryCatalog catalog,
    LocalizationAuthoringService authoring, IEditorLocalizationService text)
{
    private readonly Dictionary<string, GameTextDraft> _drafts = new(StringComparer.Ordinal);
    public IEditorLocalizationService Text => text;
    public event Action? Changed { add => content.Changed += value; remove => content.Changed -= value; }
    public string Culture => content.CaptureWorking().Configuration.ReadDefaultOptions().TargetLocale.Code;
    public string? Read(string key) => string.IsNullOrEmpty(key) ? null : query.Get()?.FindDirect(Culture, key)?.Text;
    public int Uses(string key) => query.Get()?.References.Count(reference => reference.Semantic == FieldValueSemantic.LocalizedText && reference.Key == key) ?? 0;
    public LocalizationTextTarget Capture(string key, string owner) => store.CaptureText(key.Length == 0 ? NewKey(owner) : key, newFile: $"I18n/{Culture}/authoring.json");
    private readonly HashSet<string> _reservedKeys = new(StringComparer.Ordinal);
    private string NewKey(string owner, LocalizationReferenceIndex? snapshot = null)
    {
        var index = snapshot ?? query.Get() ?? throw new ObjectDisposedException(nameof(GameTextAuthoringService));
        string key;
        do { key = "text_" + owner + "_" + Guid.NewGuid().ToString("N"); }
        while (_reservedKeys.Contains(key) || index.Texts.Any(value => value.Key == key) || index.References.Any(value => value.Key == key));
        _reservedKeys.Add(key); return key;
    }
    public GameTextDraft Register(LocalizationTextTarget target, string value)
    {
        var draft = new GameTextDraft("edit_" + Guid.NewGuid().ToString("N"), target.Key, target, value);
        _drafts.Add(draft.Token, draft); return draft;
    }
    public void Release(GameTextDraft draft) => _drafts.Remove(draft.Token);
    public Task<IReadOnlyList<LocalizationFilePatch>> PrepareTextAsync(LocalizationTextTarget target, string value, IReadOnlyList<LocalizationFilePatch> existing, Action current) =>
        PrepareFilesAsync([new("source", target.Key, target, value)], existing, current);
    public IReadOnlyList<LocalizationReference> References(string group, EditorEntryData entry) => new LocalizationReferenceIndex(
        new() { GroupEntries = new() { [group] = [entry] } }, catalog, []).References.Where(reference => reference.Semantic == FieldValueSemantic.LocalizedText).ToArray();

    public async Task<GameTextChange?> PrepareEntryAsync(string group, EditorEntryData entry, Dictionary<string, JsonElement> parameters,
        IReadOnlyList<LocalizationFilePatch> existing, Action current)
    {
        var candidate = new EditorEntryData { StableId = entry.StableId, Type = entry.Type, Parameters = parameters };
        var references = References(group, candidate).Where(reference => _drafts.ContainsKey(reference.Key)).ToArray();
        if (references.Length == 0) return null;
        var drafts = references.Select(reference => _drafts[reference.Key]).DistinctBy(draft => draft.Token).ToArray();
        var files = await PrepareFilesAsync(drafts, existing, current);
        var root = JsonSerializer.SerializeToNode(parameters)!;
        foreach (var reference in references) Set(root, reference.JsonPointer, _drafts[reference.Key].Key);
        return new(root.Deserialize<Dictionary<string, JsonElement>>()!, files);
    }
    public async Task<bool> CommitEntryAsync(string group, EditorEntryData entry, string type, Dictionary<string, JsonElement> parameters, Func<bool> isCurrent, CancellationToken token)
    {
        var before = entry.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
        void Current()
        {
            token.ThrowIfCancellationRequested();
            if (!isCurrent() || entry.Type != type || !LocalizationEditOperation.Same(entry.Parameters, before)) throw new EditorOperationException("entry.obsolete", "The text field changed before submission.");
        }
        Current(); var change = await PrepareEntryAsync(group, entry, parameters, [], Current);
        if (change is null) return false;
        Current(); catalog.Get(type).DynamicParameters.ValidatePartialArguments(JsonSerializer.SerializeToElement(change.Parameters.Where(pair => catalog.Get(type).DynamicParameters.ContainsKey(pair.Key) &&
            (!before.TryGetValue(pair.Key, out var previous) || !JsonElement.DeepEquals(previous, pair.Value))).ToDictionary(pair => pair.Key, pair => pair.Value)));
        if (change.Files.Count == 0 && LocalizationEditOperation.Same(before, change.Parameters)) return true;
        await authoring.ApplyAsync(store.PrepareWithEntries(change.Files, [new(group, entry.StableId, type, before, change.Parameters)]), entry.StableId, "History.GameText");
        return true;
    }

    private async Task<IReadOnlyList<LocalizationFilePatch>> PrepareFilesAsync(IEnumerable<GameTextDraft> values, IReadOnlyList<LocalizationFilePatch> existing, Action current)
    {
        var admittedCulture = Culture; var admittedCurrent = current;
        current = () => { admittedCurrent(); if (Culture != admittedCulture) throw new EditorOperationException("text.localeChanged", text["Error.text.localeChanged"]); };
        var drafts = values.ToArray(); current();
        if (drafts.GroupBy(draft => draft.Key, StringComparer.Ordinal).Any(group => group.Select(draft => draft.Text).Distinct(StringComparer.Ordinal).Skip(1).Any()))
            throw new EditorOperationException("text.conflict", text["Error.text.conflict"]);
        if (drafts.Any(draft => draft.Target.Culture != Culture)) throw new EditorOperationException("text.localeChanged", text["Error.text.localeChanged"]);
        var metadata = store.CaptureFile(LocalizationJson.MetadataPath);
        var translationTargets = drafts.SelectMany(draft => (draft.Translations ?? new Dictionary<string, string>()).Keys.Where(culture => culture != Culture)
            .Select(culture => (draft.Token, Culture: culture, Target: store.CaptureText(draft.Key, culture)))).ToArray();
        var allFiles = drafts.Select(draft => draft.Target.File).Concat(translationTargets.Select(target => target.Target.File)).Append(metadata).ToArray();
        var missing = allFiles.Where(file => !file.Exists).DistinctBy(file => file.Path, ProjectFilePaths.Comparer).ToArray();
        foreach (var file in allFiles.Where(file => file.Exists).DistinctBy(file => file.Id))
        { await content.CommitFileDraftsAsync(file.Id); current(); }
        if (missing.Length > 0) { await store.Prepare(missing.Select(file => new LocalizationFilePatch(file, file.Data)).ToArray()).ApplyAsync(true); current(); }
        var patches = existing.ToDictionary(patch => patch.Before.Path, ProjectFilePaths.Comparer);
        void Add(LocalizationFilePatch patch)
        {
            if (patches.TryGetValue(patch.Before.Path, out var old))
            {
                // All intents in one submission start from the same snapshot.
                if (!old.Before.Data.AsSpan().SequenceEqual(patch.Before.Data)) throw ProjectFilePaths.Error("changed", patch.Before.Path);
                patches[patch.Before.Path] = patch with { Before = old.Before };
            }
            else patches.Add(patch.Before.Path, patch);
        }
        var meta = store.CaptureFile(LocalizationJson.MetadataPath); var metaBytes = meta.Data;
        foreach (var draft in drafts)
        {
            var target = draft.Target;
            if (!target.File.Exists) target = target with { File = store.CaptureFile(target.File.Path) };
            if (draft.WriteSource)
            {
                var patch = store.PrepareText(target, draft.Text);
                if (patches.TryGetValue(patch.Before.Path, out var accumulated)) patch = patch with { After = LocalizationJson.SetText(accumulated.After, draft.Key, draft.Text) };
                Add(patch);
            }
            var translated = query.Get()!.Texts.Where(item => item.Key == draft.Key && item.Culture != Culture).Select(item => item.Culture).ToArray();
            if (draft.WriteSource) metaBytes = LocalizationTextState.Update(metaBytes, draft.Key, Culture, target.Text, draft.Text, translated.Concat(draft.Translations?.Keys ?? []));
            if (draft.State is { } state && state.TryGetProperty("sourceLocale", out var sourceLocale) && sourceLocale.GetString() == Culture)
            {
                var metaRoot = JsonNode.Parse(LocalizationJson.Decode(metaBytes))!; metaRoot["textStates"] ??= new JsonObject();
                metaRoot["textStates"]![draft.Key] = JsonNode.Parse(state.GetRawText()); metaBytes = LocalizationJson.WriteLike(metaBytes, metaRoot);
            }
            foreach (var (culture, translation) in draft.Translations ?? new Dictionary<string, string>())
            {
                if (culture == Culture) continue;
                var translatedTarget = translationTargets.Single(item => item.Token == draft.Token && item.Culture == culture).Target;
                if (!translatedTarget.File.Exists) translatedTarget = translatedTarget with { File = store.CaptureFile(translatedTarget.File.Path) };
                var translatedPatch = store.PrepareText(translatedTarget, translation);
                if (patches.TryGetValue(translatedPatch.Before.Path, out var previous)) translatedPatch = translatedPatch with { After = LocalizationJson.SetText(previous.After, draft.Key, translation) };
                Add(translatedPatch);
            }
        }
        if (!meta.Data.AsSpan().SequenceEqual(metaBytes)) Add(new(meta, metaBytes));
        return patches.Values.Where(patch => !patch.Before.Data.AsSpan().SequenceEqual(patch.After)).ToArray();
    }

    private IReadOnlyList<LocalizationReference> CopyReferences(IReadOnlyList<EditorEntryData> entries, IReadOnlyList<EditorGraphNodeDto> nodes) =>
        new LocalizationReferenceIndex(new() { GroupEntries = new() { ["clipboard"] = entries.ToList() }, Graph = new() { Nodes = nodes.ToList() } }, catalog, [])
            .References.Where(reference => reference.Semantic == FieldValueSemantic.LocalizedText).ToArray();
    public GameTextClipboard CaptureCopies(IReadOnlyList<EditorEntryData> entries, IReadOnlyList<EditorGraphNodeDto>? nodes = null)
    {
        var index = query.Get()!; var keys = CopyReferences(entries, nodes ?? []).Select(reference => reference.Key).Distinct(StringComparer.Ordinal).ToArray();
        var texts = keys.ToDictionary(key => key, key => index.Texts.Where(item => item.Key == key).Select(item => item.Culture).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(culture => culture, culture => index.FindDirect(culture, key)!.Text, StringComparer.OrdinalIgnoreCase), StringComparer.Ordinal);
        var metadata = store.CaptureFile(LocalizationJson.MetadataPath);
        using var json = JsonDocument.Parse(LocalizationJson.Decode(metadata.Data));
        var states = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (json.RootElement.TryGetProperty("textStates", out var saved))
            foreach (var key in keys) if (saved.TryGetProperty(key, out var state)) states[key] = state.Clone();
        return new(texts, states);
    }
    public static void ValidateClipboard(GameTextClipboard value)
    {
        if (value?.Texts is null || value.States is null || value.Texts.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null || pair.Value.Any(item => item.Value is null))) throw new JsonException("Invalid text clipboard.");
        foreach (var language in value.Texts.Values.SelectMany(values => values.Keys))
            if (System.Globalization.CultureInfo.GetCultureInfo(language).Name != language) throw new JsonException("Use canonical clipboard cultures.");
        var metadata = JsonSerializer.SerializeToUtf8Bytes(new { version = 1, characterHighWater = 0, textStates = value.States });
        LocalizationJson.Validate(LocalizationJson.MetadataPath, metadata);
    }
    public async Task<IAsyncUndoableEdit?> PrepareCopiesAsync(GameTextClipboard clipboard, IReadOnlyList<EditorEntryData> entries,
        IReadOnlyList<EditorGraphNodeDto>? nodes, Action current)
    {
        ValidateClipboard(clipboard); var options = nodes ?? []; var references = CopyReferences(entries, options);
        var sourceIndex = query.Get()!; var admittedCulture = Culture; var admittedCurrent = current;
        current = () => { admittedCurrent(); if (Culture != admittedCulture) throw new EditorOperationException("text.localeChanged", text["Error.text.localeChanged"]); };
        var map = references.Select(reference => reference.Key).Distinct(StringComparer.Ordinal).ToDictionary(key => key, _ => NewKey("copy", sourceIndex), StringComparer.Ordinal);
        if (map.Count == 0) return null;
        if (map.Keys.Any(key => !clipboard.Texts.ContainsKey(key))) throw new JsonException("Clipboard text snapshots are incomplete.");
        var targets = map.SelectMany(pair => clipboard.Texts[pair.Key].Select(value => (Old: pair.Key, Key: pair.Value, Culture: value.Key, Text: value.Value,
            Target: store.CaptureText(pair.Value, value.Key)))).ToArray();
        var metadata = store.CaptureFile(LocalizationJson.MetadataPath);
        foreach (var file in targets.Select(target => target.Target.File).Append(metadata).Where(file => file.Exists).DistinctBy(file => file.Id))
        { await content.CommitFileDraftsAsync(file.Id); current(); }
        var missing = targets.Select(target => target.Target.File).Append(metadata).Where(file => !file.Exists).DistinctBy(file => file.Path, ProjectFilePaths.Comparer).ToArray();
        if (missing.Length > 0) { await store.Prepare(missing.Select(file => new LocalizationFilePatch(file, file.Data)).ToArray()).ApplyAsync(true); current(); }
        var patches = new Dictionary<string, LocalizationFilePatch>(ProjectFilePaths.Comparer);
        foreach (var target in targets)
        {
            var expected = target.Target;
            if (!expected.File.Exists) expected = expected with { File = store.CaptureFile(expected.File.Path) };
            var patch = store.PrepareText(expected, target.Text);
            if (patches.TryGetValue(patch.Before.Path, out var before)) patch = patch with { Before = before.Before, After = LocalizationJson.SetText(before.After, target.Key, target.Text) };
            patches[patch.Before.Path] = patch;
        }
        metadata = store.CaptureFile(LocalizationJson.MetadataPath); var bytes = metadata.Data;
        foreach (var (old, key) in map)
        {
            if (clipboard.Texts[old].TryGetValue(Culture, out var source))
                bytes = LocalizationTextState.Update(bytes, key, Culture, null, source, clipboard.Texts[old].Keys);
            if (clipboard.States.TryGetValue(old, out var state) && state.TryGetProperty("sourceLocale", out var locale) && locale.GetString() == Culture)
            {
                var root = JsonNode.Parse(LocalizationJson.Decode(bytes))!;
                root["textStates"] ??= new JsonObject(); root["textStates"]![key] = JsonNode.Parse(state.GetRawText()); bytes = LocalizationJson.WriteLike(bytes, root);
            }
        }
        if (!bytes.AsSpan().SequenceEqual(metadata.Data)) patches[metadata.Path] = new(metadata, bytes);
        current();
        foreach (var entry in entries)
        {
            var root = JsonSerializer.SerializeToNode(entry.Parameters)!;
            foreach (var reference in References("clipboard", entry)) Set(root, reference.JsonPointer, map[reference.Key]);
            entry.Parameters = root.Deserialize<Dictionary<string, JsonElement>>()!;
        }
        foreach (var option in options.SelectMany(node => node.Options ?? [])) if (map.TryGetValue(option.Text, out var key)) option.Text = key;
        return authoring.CreateEdit(store.Prepare(patches.Values.ToArray()), "copy", "History.GameText");
    }

    public FieldCopy StageCopy(ValueDescriptor descriptor, JsonElement value)
    {
        var fields = new DynamicParameterTable([new(new FieldDescriptor("value", descriptor))]);
        var copyCatalog = new TargetProfileEntryCatalog([new DefaultEntryModule("editor", [new DefaultPrimitiveEntryBase("editor.copy", fields)])]);
        var entry = new EditorEntryData { StableId = "copy", Type = "editor.copy", Parameters = new() { ["value"] = value.Clone() } };
        var references = new LocalizationReferenceIndex(new() { GroupEntries = new() { ["copy"] = [entry] } }, copyCatalog, [])
            .References.Where(reference => reference.Semantic == FieldValueSemantic.LocalizedText).ToArray();
        var root = JsonSerializer.SerializeToNode(entry.Parameters)!; var drafts = new List<GameTextDraft>(); var index = query.Get()!;
        foreach (var group in references.GroupBy(reference => reference.Key, StringComparer.Ordinal))
        {
            var key = NewKey("item", index);
            var translations = index.Texts.Where(item => item.Key == group.Key).Select(item => item.Culture).Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(culture => culture, culture => index.FindDirect(culture, group.Key)!.Text, StringComparer.OrdinalIgnoreCase);
            using var metadata = JsonDocument.Parse(LocalizationJson.Decode(store.CaptureFile(LocalizationJson.MetadataPath).Data));
            JsonElement? state = metadata.RootElement.TryGetProperty("textStates", out var states) && states.TryGetProperty(group.Key, out var existing) ? existing.Clone() : null;
            var target = store.CaptureText(key); var draft = Register(target, translations.GetValueOrDefault(Culture) ?? "") with { Translations = translations, State = state, WriteSource = translations.ContainsKey(Culture) };
            _drafts[draft.Token] = draft; drafts.Add(draft);
            foreach (var reference in group) Set(root, reference.JsonPointer, draft.Token);
        }
        return new(root["value"]!.Deserialize<JsonElement>(), () => { foreach (var draft in drafts) Release(draft); });
    }

    public IFieldBinding BranchBinding(string nodeId, EditorGraphBranchOptionDto option) => new OptionBinding(this, nodeId, option);
    private bool CurrentOption(string node, EditorGraphBranchOptionDto option) => documents.IsLoaded && documents.CurrentDocument.Nodes.Any(item => item.Id == node && item.Options?.Any(value => ReferenceEquals(value, option)) == true);
    private async Task<ValueIssue?> CommitOptionAsync(string node, EditorGraphBranchOptionDto option, FieldValue value, CancellationToken token)
    {
        var before = option.Text;
        void Current() { token.ThrowIfCancellationRequested(); if (!CurrentOption(node, option) || option.Text != before) throw new EditorOperationException("graph.branchChanged", "The option changed."); }
        Current(); var raw = value.Json is { ValueKind: JsonValueKind.String } json ? json.GetString()! : "";
        var files = _drafts.TryGetValue(raw, out var draft) ? await PrepareFilesAsync([draft], [], Current) : [];
        var after = draft?.Key ?? raw; Current();
        if (before == after && files.Count == 0) return null;
        await authoring.ApplyAsync(store.PrepareWithOptions(files, [new(node, option.Id, before, after)]), option.Id, "History.GameText"); return null;
    }
    private sealed class OptionBinding(GameTextAuthoringService owner, string node, EditorGraphBranchOptionDto option) : IFieldBinding, IEntryFieldOrigin
    {
        public string? GroupId => node;
        public string EntryId => option.Id;
        public object HostContext => this;
        public bool IsEnabled => owner.CurrentOption(node, option);
        public FieldValue Read() => new(JsonSerializer.SerializeToElement(option.Text));
        public event EventHandler? Changed { add { _changed += value; if (!_subscribed) { owner.Changed += Refresh; _subscribed = true; } } remove { _changed -= value; if (_changed is null && _subscribed) { owner.Changed -= Refresh; _subscribed = false; } } }
        private EventHandler? _changed; private bool _subscribed;
        private void Refresh() => _changed?.Invoke(this, EventArgs.Empty);
        public Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken cancellationToken) => owner.CommitOptionAsync(node, option, value, cancellationToken);
    }
    public static void Set(JsonNode root, string pointer, string value)
    {
        var parts = pointer.Split('/').Skip(1).Select(part => part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)).ToArray();
        var parent = root;
        foreach (var part in parts.SkipLast(1)) parent = parent is JsonArray array ? array[int.Parse(part, System.Globalization.CultureInfo.InvariantCulture)]! : parent[part]!;
        if (parent is JsonArray list) list[int.Parse(parts[^1], System.Globalization.CultureInfo.InvariantCulture)] = value; else parent[parts[^1]] = value;
    }
}
