using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using System.Text.Json;

namespace GalNet.Editor.Shared.Services;

public sealed record LocalizationEntryPatch(string GroupId, string EntryId, string Type,
    IReadOnlyDictionary<string, JsonElement> Before, IReadOnlyDictionary<string, JsonElement> After);

/// <summary>Data-only atomic edit; a GUI history adapter owns its execution and undo position.</summary>
public sealed class LocalizationEditOperation
{
    private readonly IProjectContentSession _content;
    private readonly Action _requireActive;
    private readonly LocalizationFilePatch[] _patches;
    private readonly LocalizationEntryPatch[] _entries;
    private readonly IEditorDocumentService? _documents;
    private readonly Dictionary<string, byte[]> _removedSaved = [];
    public long RetainedBytes => _patches.Sum(patch => patch.Before.Data.LongLength + patch.After.LongLength) + _removedSaved.Values.Sum(bytes => bytes.LongLength)
        + JsonSerializer.SerializeToUtf8Bytes(_entries).LongLength;

    internal LocalizationEditOperation(IProjectContentSession content, Action requireActive, IEnumerable<LocalizationFilePatch> patches,
        IEditorDocumentService? documents = null, IEnumerable<LocalizationEntryPatch>? entries = null)
    {
        _content = content; _requireActive = requireActive;
        _documents = documents;
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
            var values = _patches.Select(patch => (Id: patch.Before.Id, Bytes: (forward ? patch.After : patch.Before.Data).ToArray())).ToArray();
            var entryValues = _entries.Select(patch => (Entry: Find(patch), Values: Clone(forward ? patch.After : patch.Before))).ToArray();
            foreach (var value in values) _content.FileDocuments[value.Id].Data = value.Bytes;
            foreach (var value in entryValues) value.Entry.Parameters = value.Values;
            _content.Refresh();
        }
    }

    private EditorEntryData Find(LocalizationEntryPatch patch) => _documents is { IsLoaded: true } &&
        _documents.Groups.TryGetValue(patch.GroupId, out var group) && group.Entries.SingleOrDefault(entry => entry.StableId == patch.EntryId) is { } entry
            ? entry : throw new EditorOperationException("entry.obsolete", "Entry no longer exists.");
    private void CheckEntries(bool forward)
    {
        foreach (var patch in _entries)
        {
            var entry = Find(patch);
            if (entry.Type != patch.Type || !Same(entry.Parameters, forward ? patch.Before : patch.After))
                throw new EditorOperationException("entry.changed", "Entry changed outside this edit.");
        }
    }
    public static bool Same(IReadOnlyDictionary<string, JsonElement> left, IReadOnlyDictionary<string, JsonElement> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && JsonElement.DeepEquals(pair.Value, value));
    private static Dictionary<string, JsonElement> Clone(IReadOnlyDictionary<string, JsonElement> values) => values.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);

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
                !ProjectFilePaths.Comparer.Equals(file.Path, before.Path) || !file.Data.AsSpan().SequenceEqual(forward ? before.Data : patch.After))
                throw ProjectFilePaths.Error("changed", before.Path);
        }
    }

    private sealed record Change(LocalizationEditOperation Owner, bool Forward, ProjectFilePlan Files) : IProjectFileStructureChange
    {
        public void Apply(EditorProjectDocument document, AuthorDocumentVersion version)
        {
            if (version == AuthorDocumentVersion.Working)
                foreach (var patch in Owner._entries)
                    document.GroupEntries[patch.GroupId].Single(entry => entry.StableId == patch.EntryId).Parameters = Clone(Forward ? patch.After : patch.Before);
            foreach (var patch in Owner._patches)
            {
                var before = patch.Before;
                if (version == AuthorDocumentVersion.Saved && before.Exists) continue;
                if (!document.Files.TryGetValue(before.Id, out var file))
                    document.Files[before.Id] = file = new() { Id = before.Id, Path = before.Path, ManagedContent = true };
                file.Exists = Forward || before.Exists;
                file.Data = (version == AuthorDocumentVersion.Working
                    ? Forward ? patch.After : before.Data
                    : Owner._removedSaved.GetValueOrDefault(before.Id) ?? before.Data).ToArray();
            }
        }
    }
}
