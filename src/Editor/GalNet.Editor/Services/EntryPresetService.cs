using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.Shared.Visuals;

namespace GalNet.Editor.Services;

public sealed record EntryPreset(string Id, string Name, string Type, Dictionary<string, JsonElement> Parameters)
{
    [JsonIgnore] public bool IsBuiltin { get; init; }
}

/// <summary>Project authoring presets publish independently of unsaved Group and settings changes.</summary>
public sealed class EntryPresetService(IProjectService projects, IProjectContentSession content, EditorHistories histories, IEntryCatalog catalog, EntryFieldAuthoringService fields)
{
    private const string RelativePath = "Presets/entries.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly IProjectContentSession _content = content;
    private sealed record Document(int Version, List<EntryPreset> Presets);
    public event Action? Changed;
    private string PathName(string path) => new ProjectRawReader(projects.Current?.RootPath ?? throw new InvalidOperationException("No project is open.")).Resolve(path);

    public IReadOnlyList<EntryPreset> List(string type) => ListAll().Where(item => item.Type == type).ToArray();
    private Document Read(string path, out byte[]? bytes)
    {
        var full = PathName(path);
        if (!File.Exists(full)) { bytes = null; return new(1, []); }
        bytes = File.ReadAllBytes(full);
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
        var document = Read(RelativePath, out var before);
        var number = 1;
        string DefaultName(int index) => EditorLocalizationService.Current?.Format("Entry.PresetName", index) ?? $"Preset {index}";
        while (document.Presets.Any(item => item.Type == entry.Type && item.Name == DefaultName(number))) number++;
        var preset = new EntryPreset(Guid.NewGuid().ToString("N"), DefaultName(number), entry.Type, EntryPresetParameters.Capture(catalog, entry.Type, entry.Parameters));
        document.Presets.Add(preset);
        await SaveAsync(RelativePath, document, before, "Add entry preset");
        return preset;
    }
    public Task RenameAsync(string id, string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("Preset name cannot be empty.");
        var path = ListAll().Any(item => item.Id == id && item.IsBuiltin) ? DefaultParticlePresets.Path : RelativePath;
        var doc = Read(path, out var before); var index = doc.Presets.FindIndex(item => item.Id == id);
        if (index < 0) throw new InvalidOperationException("Preset no longer exists.");
        if (doc.Presets[index].Name == name) return Task.CompletedTask;
        doc.Presets[index] = doc.Presets[index] with { Name = name };
        return SaveAsync(path, doc, before, "Rename entry preset");
    }
    public Task DeleteAsync(string id)
    {
        if (ListAll().Any(item => item.Id == id && item.IsBuiltin)) throw new InvalidOperationException("Builtin presets cannot be deleted.");
        var doc = Read(RelativePath, out var before);
        if (doc.Presets.RemoveAll(item => item.Id == id) != 1) throw new InvalidOperationException("Preset no longer exists.");
        return SaveAsync(RelativePath, doc, before, "Delete entry preset");
    }
    public async Task ApplyAsync(string groupId, EditorEntryData entry, string id)
    {
        await content.CommitDraftsAsync();
        if (!fields.IsCurrent(groupId, entry)) throw new InvalidOperationException("The entry is no longer available.");
        var preset = List(entry.Type).SingleOrDefault(item => item.Id == id) ?? throw new InvalidOperationException("Preset is unavailable for this entry.");
        fields.SetParameters(groupId, entry, entry.Type, EntryPresetParameters.Apply(catalog, entry.Type, preset.Parameters, entry.Parameters));
    }
    private IReadOnlyList<EntryPreset> ListAll()
    {
        var builtins = Read(DefaultParticlePresets.Path, out var bytes).Presets;
        var required = catalog.TryGet(GalNet.Contracts.Builtins.BuiltinEntryIds.ApplyEffect, out _) &&
            projects.Current?.Configurations.Sections.Any(section => section.Id == GalNet.Contracts.Builtins.DefaultProjectConfiguration.Section.Id) == true;
        if (bytes is null && (required || Directory.Exists(PathName("Buildin/Effects/Default"))))
            throw new InvalidDataException("Builtin particle preset file is missing: " + DefaultParticlePresets.Path);
        if (bytes is not null)
        {
            var metadata = PathName(DefaultParticlePresets.Path + ".meta");
            if (!File.Exists(metadata)) throw new InvalidDataException("Builtin particle preset metadata is missing.");
            using var json = JsonDocument.Parse(File.ReadAllBytes(metadata));
            if (!json.RootElement.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(id.GetString(), out var guid) || guid != Guid.Parse(DefaultParticlePresets.AssetId) ||
                !json.RootElement.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "data")
                throw new InvalidDataException("Builtin particle preset metadata has a different identity or type.");
            foreach (var presetId in DefaultParticlePresets.Ids)
                if (!builtins.Any(item => item.Id == presetId && item.Type == GalNet.Contracts.Builtins.BuiltinEntryIds.PlayParticleEmitter))
                    throw new InvalidDataException("Builtin particle preset is missing or has a different identity.");
        }
        var all = builtins.Select(item => item with { IsBuiltin = true }).Concat(Read(RelativePath, out _).Presets).ToArray();
        if (all.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != all.Length) throw new InvalidDataException("Duplicate Entry preset IDs.");
        return all;
    }
    private async Task SaveAsync(string path, Document document, byte[]? before, string description)
    {
        var tracked = content.FileDocuments.Values.FirstOrDefault(file => file.ManagedContent && ProjectFilePaths.Comparer.Equals(file.Path, path));
        if (tracked is not null) await content.CommitFileDraftsAsync(tracked.Id);
        var after = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        await histories.Content.ExecuteAsync(new PresetEdit(this, path, before, after, description));
    }
    private sealed record PresetEdit(EntryPresetService Owner, string Path, byte[]? Before, byte[] After, string Description) : IAsyncUndoableEdit
    {
        public long RetainedBytes => (Before?.LongLength ?? 0) + After.LongLength;
        public ValueTask RedoAsync() => Publish(Before, After);
        public ValueTask UndoAsync() => Publish(After, Before);
        private async ValueTask Publish(byte[]? expected, byte[]? result)
        {
            await Owner._content.PublishStructureAsync((working, saved) =>
            {
                var tracked = working.Files.Values.Concat(saved.Files.Values).Where(file => file.ManagedContent && ProjectFilePaths.Comparer.Equals(file.Path, Path)).ToArray();
                if (tracked.Any(file => file.Exists != (expected is not null) || file.Exists && !file.Data.AsSpan().SequenceEqual(expected)))
                    throw new InvalidOperationException("The preset file has unsaved edits. Save or discard them before changing presets.");
                var plan = new ProjectFilePlan();
                plan.ExpectedHashes[Path] = expected is null ? null : Convert.ToHexString(SHA256.HashData(expected));
                if (tracked.Length == 0)
                {
                    if (result is null) plan.Deletes.Add(Path);
                    else plan.Writes[Path] = result;
                }
                if (result is not null) plan.CreateDirectories.Add(Path[..Path.LastIndexOf('/')]);
                return new PresetChange(plan, Path, result);
            });
            Owner.Changed?.Invoke();
        }
    }
    private sealed record PresetChange(ProjectFilePlan Files, string Path, byte[]? Result) : IProjectFileStructureChange
    {
        public void Apply(EditorProjectDocument document, AuthorDocumentVersion version)
        {
            foreach (var file in document.Files.Values.Where(file => file.ManagedContent && ProjectFilePaths.Comparer.Equals(file.Path, Path)))
            { file.Exists = Result is not null; file.Data = Result?.ToArray() ?? []; }
        }
    }
}
