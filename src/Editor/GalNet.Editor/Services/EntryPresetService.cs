using System.Security.Cryptography;
using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

public sealed record EntryPreset(string Id, string Name, string Type, Dictionary<string, JsonElement> Parameters);

/// <summary>Project authoring presets publish independently of unsaved Group and settings changes.</summary>
public sealed class EntryPresetService(IProjectService projects, IProjectContentSession content, EditorHistories histories, IEntryCatalog catalog, EntryFieldAuthoringService fields)
{
    private const string RelativePath = "Presets/entries.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly IProjectContentSession _content = content;
    private sealed record Document(int Version, List<EntryPreset> Presets);
    public event Action? Changed;
    private string PathName => new ProjectRawReader(projects.Current?.RootPath ?? throw new InvalidOperationException("No project is open.")).Resolve(RelativePath);

    public IReadOnlyList<EntryPreset> List(string type) => Read(out _).Presets.Where(item => item.Type == type).ToArray();
    private Document Read(out byte[]? bytes)
    {
        if (!File.Exists(PathName)) { bytes = null; return new(1, []); }
        bytes = File.ReadAllBytes(PathName);
        var result = JsonSerializer.Deserialize<Document>(bytes, JsonOptions) ?? throw new InvalidDataException("Empty Entry preset file.");
        if (result.Version != 1 || result.Presets is null || result.Presets.Any(item => item is null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Type) || item.Parameters is null) || result.Presets.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != result.Presets.Count)
            throw new InvalidDataException("Invalid Entry preset file.");
        return result;
    }
    public async Task<EntryPreset> AddAsync(string groupId, EditorEntryData entry)
    {
        var type = entry.Type;
        await content.CommitDraftsAsync();
        if (!fields.IsCurrent(groupId, entry) || entry.Type != type || !catalog.TryGet(type, out _)) throw new InvalidOperationException("The entry is no longer available.");
        var document = Read(out var before);
        var number = 1;
        string DefaultName(int index) => EditorLocalizationService.Current?.Format("Entry.PresetName", index) ?? $"Preset {index}";
        while (document.Presets.Any(item => item.Type == entry.Type && item.Name == DefaultName(number))) number++;
        var preset = new EntryPreset(Guid.NewGuid().ToString("N"), DefaultName(number), entry.Type, EntryPresetParameters.Capture(catalog, entry.Type, entry.Parameters));
        document.Presets.Add(preset);
        await SaveAsync(document, before, "Add entry preset");
        return preset;
    }
    public Task RenameAsync(string id, string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("Preset name cannot be empty.");
        var doc = Read(out var before); var index = doc.Presets.FindIndex(item => item.Id == id);
        if (index < 0) throw new InvalidOperationException("Preset no longer exists.");
        if (doc.Presets[index].Name == name) return Task.CompletedTask;
        doc.Presets[index] = doc.Presets[index] with { Name = name };
        return SaveAsync(doc, before, "Rename entry preset");
    }
    public Task DeleteAsync(string id)
    {
        var doc = Read(out var before);
        if (doc.Presets.RemoveAll(item => item.Id == id) != 1) throw new InvalidOperationException("Preset no longer exists.");
        return SaveAsync(doc, before, "Delete entry preset");
    }
    public async Task ApplyAsync(string groupId, EditorEntryData entry, string id)
    {
        await content.CommitDraftsAsync();
        if (!fields.IsCurrent(groupId, entry)) throw new InvalidOperationException("The entry is no longer available.");
        var preset = Read(out _).Presets.SingleOrDefault(item => item.Id == id && item.Type == entry.Type) ?? throw new InvalidOperationException("Preset is unavailable for this entry.");
        fields.SetParameters(groupId, entry, entry.Type, EntryPresetParameters.Apply(catalog, entry.Type, preset.Parameters, entry.Parameters));
    }
    private async Task SaveAsync(Document document, byte[]? before, string description)
    {
        var tracked = content.FileDocuments.Values.FirstOrDefault(file => file.ManagedContent && ProjectFilePaths.Comparer.Equals(file.Path, RelativePath));
        if (tracked is not null) await content.CommitFileDraftsAsync(tracked.Id);
        var after = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        await histories.Content.ExecuteAsync(new PresetEdit(this, before, after, description));
    }
    private sealed record PresetEdit(EntryPresetService Owner, byte[]? Before, byte[] After, string Description) : IAsyncUndoableEdit
    {
        public long RetainedBytes => (Before?.LongLength ?? 0) + After.LongLength;
        public ValueTask RedoAsync() => Publish(Before, After);
        public ValueTask UndoAsync() => Publish(After, Before);
        private async ValueTask Publish(byte[]? expected, byte[]? result)
        {
            await Owner._content.PublishStructureAsync((working, saved) =>
            {
                var tracked = working.Files.Values.Concat(saved.Files.Values).Where(file => file.ManagedContent && ProjectFilePaths.Comparer.Equals(file.Path, RelativePath)).ToArray();
                if (tracked.Any(file => file.Exists != (expected is not null) || file.Exists && !file.Data.AsSpan().SequenceEqual(expected)))
                    throw new InvalidOperationException("The preset file has unsaved edits. Save or discard them before changing presets.");
                var plan = new ProjectFilePlan();
                plan.ExpectedHashes[RelativePath] = expected is null ? null : Convert.ToHexString(SHA256.HashData(expected));
                if (tracked.Length == 0)
                {
                    if (result is null) plan.Deletes.Add(RelativePath);
                    else plan.Writes[RelativePath] = result;
                }
                if (result is not null) plan.CreateDirectories.Add("Presets");
                return new PresetChange(plan, result);
            });
            Owner.Changed?.Invoke();
        }
    }
    private sealed record PresetChange(ProjectFilePlan Files, byte[]? Result) : IProjectFileStructureChange
    {
        public void Apply(EditorProjectDocument document, AuthorDocumentVersion version)
        {
            foreach (var file in document.Files.Values.Where(file => file.ManagedContent && ProjectFilePaths.Comparer.Equals(file.Path, RelativePath)))
            { file.Exists = Result is not null; file.Data = Result?.ToArray() ?? []; }
        }
    }
}
