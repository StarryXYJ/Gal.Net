using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.Shared.Services;

public sealed record LocalizationEntryPatch(string GroupId, string EntryId, string Type,
    IReadOnlyDictionary<string, JsonElement> Before, IReadOnlyDictionary<string, JsonElement> After);
public sealed record LocalizationOptionPatch(string NodeId, string OptionId, string Before, string After);

/// <summary>Data-only atomic edit; a GUI history adapter owns its execution and undo position.</summary>
public sealed class LocalizationEditOperation
{
    private readonly IProjectContentSession _content;
    private readonly Action _requireActive;
    private readonly LocalizationFilePatch[] _patches;
    private readonly LocalizationEntryPatch[] _entries;
    private readonly LocalizationOptionPatch[] _options;
    private readonly IEditorDocumentService? _documents;
    private readonly Dictionary<string, byte[]> _removedSaved = [];
    public long RetainedBytes => _patches.Sum(patch => patch.Before.Data.LongLength + patch.After.LongLength) + _removedSaved.Values.Sum(bytes => bytes.LongLength)
        + JsonSerializer.SerializeToUtf8Bytes(_entries).LongLength + JsonSerializer.SerializeToUtf8Bytes(_options).LongLength;

    internal LocalizationEditOperation(IProjectContentSession content, Action requireActive, IEnumerable<LocalizationFilePatch> patches,
        IEditorDocumentService? documents = null, IEnumerable<LocalizationEntryPatch>? entries = null, IEnumerable<LocalizationOptionPatch>? options = null)
    {
        _content = content; _requireActive = requireActive;
        _documents = documents;
        _options = (options ?? []).ToArray();
        if (_options.Length > 0 && (documents is null || _options.Select(option => (option.NodeId, option.OptionId)).Distinct().Count() != _options.Length))
            throw new ArgumentException("Option targets must be unique and belong to a document service.", nameof(options));
        _entries = (entries ?? []).Select(entry => entry with { Before = Clone(entry.Before), After = Clone(entry.After) }).ToArray();
        if (_entries.Length > 0 && (documents is null || _entries.Select(entry => (entry.GroupId, entry.EntryId)).Distinct().Count() != _entries.Length))
            throw new ArgumentException("Entry targets must be unique and belong to a document service.", nameof(entries));
        _patches = patches.Select(patch => new LocalizationFilePatch(patch.Before with { Path = LocalizationJson.Path(patch.Before.Path), Data = patch.Before.Data.ToArray() }, patch.After.ToArray())).ToArray();
        if (_patches.Any(patch => string.IsNullOrWhiteSpace(patch.Before.Id)) || _patches.Select(patch => patch.Before.Id).Distinct(StringComparer.Ordinal).Count() != _patches.Length)
            throw new ArgumentException("File identities must be nonempty and unique.", nameof(patches));
        if (_patches.Select(patch => patch.Before.Path).Distinct(ProjectFilePaths.Comparer).Count() != _patches.Length)
            throw new ArgumentException("Combine changes to one file before preparing an operation.", nameof(patches));
        foreach (var patch in _patches)
        {
            LocalizationJson.Validate(patch.Before.Path, patch.Before.Data);
            LocalizationJson.Validate(patch.Before.Path, patch.After);
        }
    }

    public async Task ApplyAsync(bool forward)
    {
        _requireActive();
        CheckEntries(forward);
        if (_patches.Any(patch => !patch.Before.Exists))
        {
            await _content.PublishStructureAsync((working, saved) =>
            {
                Check(working.Files, forward);
                CheckEntries(forward);
                if (!forward)
                    foreach (var patch in _patches.Where(patch => !patch.Before.Exists))
                        if (saved.Files.TryGetValue(patch.Before.Id, out var file) && file.Exists) _removedSaved[file.Id] = file.Data.ToArray();
                var files = new ProjectFilePlan();
                foreach (var patch in _patches.Where(patch => !patch.Before.Exists))
                    if (forward) files.ExpectedHashes[patch.Before.Path] = null;
                return new Change(this, forward, files);
            });
        }
        else
        {
            Check(_content.FileDocuments, forward);
            var values = _patches.Select(patch => (Id: patch.Before.Id, Bytes: Value(patch.Before.Path, forward ? patch.After : patch.Before.Data, _content.FileDocuments[patch.Before.Id].Data))).ToArray();
            var entryValues = _entries.Select(patch => (Entry: Find(patch), Values: Clone(forward ? patch.After : patch.Before))).ToArray();
            var optionValues = _options.Select(patch => (Option: FindOption(patch), Value: forward ? patch.After : patch.Before)).ToArray();
            foreach (var value in values) _content.FileDocuments[value.Id].Data = value.Bytes;
            foreach (var value in entryValues) value.Entry.Parameters = value.Values;
            foreach (var value in optionValues) value.Option.Text = value.Value;
            _content.Refresh();
        }
    }

    private EditorEntryData Find(LocalizationEntryPatch patch) => _documents is { IsLoaded: true } &&
        _documents.Groups.TryGetValue(patch.GroupId, out var group) && group.Entries.SingleOrDefault(entry => entry.StableId == patch.EntryId) is { } entry
            ? entry : throw new EditorOperationException("entry.obsolete", "Entry no longer exists.");
    private void CheckEntries(bool forward)
    {
        foreach (var patch in _options)
            if (FindOption(patch).Text != (forward ? patch.Before : patch.After))
                throw new EditorOperationException("graph.branchChanged", "The option changed outside this edit.");
        foreach (var patch in _entries)
        {
            var entry = Find(patch);
            if (entry.Type != patch.Type || !Same(entry.Parameters, forward ? patch.Before : patch.After))
                throw new EditorOperationException("entry.changed", "Entry changed outside this edit.");
        }
    }
    private EditorGraphBranchOptionDto FindOption(LocalizationOptionPatch patch) => _documents is { IsLoaded: true } &&
        _documents.CurrentDocument.Nodes.SingleOrDefault(node => node.Id == patch.NodeId && node.Type == "Branch" && node.BranchType == "Choice")?.Options?
            .SingleOrDefault(option => option.Id == patch.OptionId) is { } option ? option : throw new EditorOperationException("graph.branchChanged", "The option no longer exists.");
    public static bool Same(IReadOnlyDictionary<string, JsonElement> left, IReadOnlyDictionary<string, JsonElement> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && JsonElement.DeepEquals(pair.Value, value));
    private static Dictionary<string, JsonElement> Clone(IReadOnlyDictionary<string, JsonElement> values) => values.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
    private static byte[] Value(string path, byte[] desired, byte[] current)
    {
        if (path != LocalizationJson.MetadataPath) return desired.ToArray();
        var node = JsonNode.Parse(LocalizationJson.Decode(desired))!;
        var actualIdentity = JsonNode.Parse(LocalizationJson.Decode(current))!["exchangeProjectId"]?.GetValue<string>();
        if (node["exchangeProjectId"] is null && actualIdentity is not null)
        {
            node["exchangeProjectId"] = actualIdentity;
            desired = LocalizationJson.WriteLike(desired, node);
        }
        var reserved = JsonNode.Parse(LocalizationJson.Decode(current))!["characterHighWater"]!.GetValue<long>();
        if (reserved <= node["characterHighWater"]!.GetValue<long>()) return desired.ToArray();
        node["characterHighWater"] = reserved;
        return LocalizationJson.WriteLike(desired, node);
    }
    private static bool Matches(string path, byte[] current, byte[] expected)
    {
        if (path != LocalizationJson.MetadataPath) return current.AsSpan().SequenceEqual(expected);
        LocalizationJson.Validate(path, current);
        var actual = JsonNode.Parse(LocalizationJson.Decode(current))!.AsObject(); var baseline = JsonNode.Parse(LocalizationJson.Decode(expected))!.AsObject();
        if (baseline["exchangeProjectId"] is null) actual.Remove("exchangeProjectId");
        if (actual["characterHighWater"]!.GetValue<long>() < baseline["characterHighWater"]!.GetValue<long>()) return false;
        actual.Remove("characterHighWater"); baseline.Remove("characterHighWater"); return JsonNode.DeepEquals(actual, baseline);
    }

    private void Check(IReadOnlyDictionary<string, EditorFileDocument> files, bool forward)
    {
        foreach (var patch in _patches)
        {
            var before = patch.Before;
            if (forward && !before.Exists)
            {
                if (files.Values.Any(file => file.Exists && ProjectFilePaths.Comparer.Equals(file.Path, before.Path))) throw ProjectFilePaths.Error("changed", before.Path);
                if (files.TryGetValue(before.Id, out var retained) && !ProjectFilePaths.Comparer.Equals(retained.Path, before.Path)) throw ProjectFilePaths.Error("changed", before.Path);
            }
            else if (!files.TryGetValue(before.Id, out var file) || !file.Exists || !file.ManagedContent ||
                !ProjectFilePaths.Comparer.Equals(file.Path, before.Path) || !Matches(before.Path, file.Data, forward ? before.Data : patch.After))
                throw ProjectFilePaths.Error("changed", before.Path);
        }
    }

    private sealed record Change(LocalizationEditOperation Owner, bool Forward, ProjectFilePlan Files) : IProjectFileStructureChange
    {
        public void Apply(EditorProjectDocument document, AuthorDocumentVersion version)
        {
            if (version == AuthorDocumentVersion.Working)
            {
                foreach (var patch in Owner._entries)
                    document.GroupEntries[patch.GroupId].Single(entry => entry.StableId == patch.EntryId).Parameters = Clone(Forward ? patch.After : patch.Before);
                foreach (var patch in Owner._options)
                    document.Graph.Nodes.Single(node => node.Id == patch.NodeId).Options!.Single(option => option.Id == patch.OptionId).Text = Forward ? patch.After : patch.Before;
            }
            foreach (var patch in Owner._patches)
            {
                var before = patch.Before;
                if (version == AuthorDocumentVersion.Saved && before.Exists) continue;
                if (!document.Files.TryGetValue(before.Id, out var file))
                    document.Files[before.Id] = file = new() { Id = before.Id, Path = before.Path, ManagedContent = true };
                file.Exists = Forward || before.Exists;
                var desired = (version == AuthorDocumentVersion.Working
                    ? Forward ? patch.After : before.Data
                    : Owner._removedSaved.GetValueOrDefault(before.Id) ?? before.Data).ToArray();
                file.Data = file.Data.Length > 0 ? Value(before.Path, desired, file.Data) : desired;
            }
        }
    }
}
