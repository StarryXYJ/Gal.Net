using GalNet.Assets;
using GalNet.Core.Assets;
using GalNet.Core.Configuration;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

public sealed class ResourceOverrideAuthoringService(IProjectService projects, IProjectContentSession content,
    EditorHistories histories, IAssetManager assets, IResourceTypeCatalog types, GalNet.Core.Entry.IEntryCatalog catalog,
    IEntryParameterMetadataSource metadata)
{
    public IProjectContentSession Content => content;
    public IAssetManager Assets => assets;
    public EditorHistories Histories => histories;
    public ResourceOverrideOptions Read() => projects.Current?.Configuration.Read(ResourceOverrideOptions.Section) ?? new();
    public async Task<ResourceReferenceIndex> SnapshotAsync(CancellationToken ct)
    {
        var document = content.CaptureWorking();
        var fields = metadata.Capture(document);
        var files = await assets.GetFilesAsync(ct: ct);
        ct.ThrowIfCancellationRequested();
        return new(document, catalog, files, fields);
    }
    public string Create(string name)
    {
        var id = Guid.NewGuid().ToString("N");
        Update(options => options.Packages.Add(new() { Id = id, Name = Name(name) }));
        return id;
    }
    public void Rename(string id, string name) => Update(options => Package(options, id).Name = Name(name));
    public void SetPriority(string id, int priority) => Update(options => Package(options, id).Priority = priority);
    public void SetHeader(string id, string name, int priority, string expectedName, int expectedPriority) => Update(options =>
    {
        var package = Package(options, id);
        if (package.Name != expectedName || package.Priority != expectedPriority) throw new InvalidOperationException("This resource patch header changed. Discard the draft and retry.");
        package.Name = Name(name); package.Priority = priority;
    });
    public void Delete(string id) => Update(options => options.Packages.Remove(Package(options, id)));
    public async Task AddOriginalAsync(string id)
    {
        var project = projects.Current;
        var file = await assets.GetFileAsync(id) ?? throw new InvalidDataException("Original resource is missing.");
        if (!ReferenceEquals(projects.Current, project) || file.TypeId is "graph" or "group") throw new InvalidDataException("Original resource is unavailable.");
        Update(options => { if (!options.AdditionalOriginals.Contains(file.Id, StringComparer.OrdinalIgnoreCase)) options.AdditionalOriginals.Add(file.Id); });
    }
    public async Task SetMappingAsync(string packageId, string sourceId, string targetId, string? expected)
    {
        var project = projects.Current;
        var revision = (assets as Interfaces.IEditorAssetChanges)?.Revision;
        targetId = targetId.Trim();
        if (targetId.Length != 0)
        {
            var original = await assets.GetFileAsync(sourceId) ?? throw new InvalidDataException("Original resource is missing.");
            var replacement = await assets.GetFileAsync(targetId) ?? throw new InvalidDataException("Replacement resource is missing.");
            if (StringComparer.OrdinalIgnoreCase.Equals(original.Id, replacement.Id)) throw new InvalidDataException("Choose a different replacement resource, or clear this cell.");
            ResourceOverrideBuilder.ValidateCompatibility(original, replacement);
        }
        if (!ReferenceEquals(project, projects.Current) || revision != (assets as Interfaces.IEditorAssetChanges)?.Revision)
            throw new InvalidOperationException("Resources changed while selecting the replacement. Choose again.");
        Update(options =>
        {
            var package = Package(options, packageId);
            if (package.Mappings.GetValueOrDefault(sourceId) != expected) throw new InvalidOperationException("This override cell changed. Choose again.");
            if (targetId.Length == 0) package.Mappings.Remove(sourceId); else package.Mappings[sourceId] = targetId;
        });
    }
    public async Task ExportAsync(string id, string target, CancellationToken ct)
    {
        var project = projects.Current ?? throw new InvalidOperationException("Open a project first.");
        await content.SaveAsync(ct);
        using var capture = await histories.Content.AcquireCaptureAsync(cancellationToken: ct);
        if (!ReferenceEquals(projects.Current, project)) throw new InvalidOperationException("The project was closed.");
        var definition = Package(content.CaptureSaved().Configuration.Read(ResourceOverrideOptions.Section), id);
        // The capture lease protects disk publication only until the private resource copy exists.
        await Task.Run(() => ResourceOverrideExporter.ExportAsync(project.Id, project.RootPath, definition, target, types, ct, capture.Dispose), ct);
    }
    private static ResourceOverrideDefinition Package(ResourceOverrideOptions options, string id) => options.Packages.SingleOrDefault(package => package.Id == id)
        ?? throw new InvalidOperationException("This resource patch no longer exists.");
    private static string Name(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Patch name is required.") : value.Trim();
    private void Update(Action<ResourceOverrideOptions> change)
    {
        var configuration = projects.Current?.Configuration ?? throw new InvalidOperationException("Open a project first.");
        var before = configuration.Sections.GetValueOrDefault(ResourceOverrideOptions.Section.Id);
        var next = configuration.Clone();
        var options = next.Read(ResourceOverrideOptions.Section); change(options); next.Set(ResourceOverrideOptions.Section, options);
        var after = next.Sections[ResourceOverrideOptions.Section.Id];
        if (!ConfigurationSectionEditor.Equal(before, after)) histories.Content.Execute(new DefinitionEdit(configuration, before, after));
    }
    private sealed record DefinitionEdit(ProjectConfiguration Configuration, ProjectSectionData? Before, ProjectSectionData After) : IUndoableEdit
    {
        public string Description => "Change resource patches";
        public long RetainedBytes => (Before?.Data.GetRawText().Length ?? 0) * 2L + After.Data.GetRawText().Length * 2L;
        public void Undo() => Apply(After, Before);
        public void Redo() => Apply(Before, After);
        private void Apply(ProjectSectionData? expected, ProjectSectionData? value)
        {
            if (!ConfigurationSectionEditor.Equal(Configuration.Sections.GetValueOrDefault(ResourceOverrideOptions.Section.Id), expected))
                throw new InvalidOperationException("Resource patch definitions changed outside this edit.");
            if (value is null) Configuration.Remove(ResourceOverrideOptions.Section.Id); else Configuration.SetRaw(ResourceOverrideOptions.Section.Id, value);
        }
    }
}
