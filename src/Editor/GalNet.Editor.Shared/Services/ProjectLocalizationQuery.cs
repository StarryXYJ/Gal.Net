using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.Shared.Services;

/// <summary>Project-scoped localization snapshots, including untracked disk sources and unsaved managed files.</summary>
public sealed class ProjectLocalizationQuery(IProjectContentSession content, IEditorDocumentService documents,
    IEntryCatalog catalog, IProjectService projects, IEntryParameterMetadataSource? parameterMetadata) : IDisposable
{
    public ProjectLocalizationQuery(IProjectContentSession content, IEditorDocumentService documents, IEntryCatalog catalog, IProjectService projects)
        : this(content, documents, catalog, projects, null) { }
    private bool _disposed;
    private string? _snapshot;
    private LocalizationReferenceIndex? _index;
    private long _metadataRevision = -1;
    public long Revision { get; private set; }

    public LocalizationReferenceIndex? Get()
    {
        if (_disposed || !documents.IsLoaded || projects.Current is null)
        { Clear(); return null; }
        var document = content.CaptureWorking();
        var sources = new Dictionary<string, LocalizationSource>(ProjectFilePaths.Comparer);
        var issues = new List<LocalizationIssue>();
        var reader = new ProjectRawReader(projects.Current.RootPath);
        var diagnostics = new List<ToolDiagnostic>();
        // Managed documents override disk, including deletion and path changes.
        var managed = document.Files.Values.GroupBy(file => file.Path, ProjectFilePaths.Comparer).ToDictionary(group => group.Key, group =>
        {
            var live = group.Where(file => file.Exists).ToArray();
            if (live.Length > 1) issues.Add(new("source.identity", group.Key, "", "Multiple live documents own the same path."));
            return live.FirstOrDefault() ?? group.First();
        }, ProjectFilePaths.Comparer);
        IReadOnlyList<string> paths = [];
        try { if (Directory.Exists(reader.Resolve("I18n"))) paths = reader.Files("I18n", diagnostics); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        { issues.Add(new("source.enumeration", "I18n", "", error.Message)); }
        foreach (var path in paths.Where(IsSource))
        {
            if (managed.ContainsKey(path)) continue;
            try { sources[path] = new(path, File.ReadAllText(reader.Resolve(path))); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
            { issues.Add(new("source.read", path, "", error.Message)); }
        }
        foreach (var file in managed.Values.Where(file => file.Exists && IsSource(file.Path)))
        {
            if (!file.ManagedContent)
            {
                try { sources[file.Path] = new(file.Path, File.ReadAllText(reader.Resolve(file.Path))); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
                { issues.Add(new("source.read", file.Path, "", error.Message)); }
            }
            else
            {
                using var stream = new MemoryStream(file.Data, writable: false);
                using var text = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                sources[file.Path] = new(file.Path, text.ReadToEnd());
            }
        }
        issues.AddRange(diagnostics.Where(diagnostic => diagnostic.File is null ||
                !diagnostic.File.StartsWith("I18n/External", StringComparison.OrdinalIgnoreCase))
            .Select(diagnostic => new LocalizationIssue("source.enumeration", diagnostic.File ?? "I18n", diagnostic.JsonPointer ?? "", diagnostic.Message)));
        var snapshot = JsonSerializer.Serialize(new { Document = document, Sources = sources.Values.OrderBy(source => source.Path, StringComparer.Ordinal), Issues = issues });
        if (snapshot != _snapshot || _metadataRevision != (parameterMetadata?.Revision ?? 0))
        {
            var metadata = parameterMetadata?.Capture(document) ?? new EntryParameterMetadata();
            _index = new(document, catalog, sources.Values, issues, metadata);
            _metadataRevision = metadata.Revision ?? parameterMetadata?.Revision ?? 0;
            _snapshot = snapshot; Revision++;
        }
        return _index;
    }

    private static bool IsSource(string path) => path.StartsWith("I18n/", StringComparison.Ordinal) &&
        !path.StartsWith("I18n/External/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    private void Clear()
    {
        if (_snapshot is not null) Revision++;
        _snapshot = null; _index = null; _metadataRevision = -1;
    }
    public void Dispose()
    {
        if (_disposed) return;
        Clear(); _disposed = true; Revision++;
    }
}
