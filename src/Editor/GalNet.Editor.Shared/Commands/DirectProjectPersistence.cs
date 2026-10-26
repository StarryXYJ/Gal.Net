using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Configuration;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Shared.Commands;

public sealed class DirectProjectPersistence(string projectPath, IEditorDocumentRepository repository, ProjectConfigurationSchema? configurations = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly string _projectPath = Path.GetFullPath(projectPath);
    private readonly IEditorDocumentRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private byte[]? _settingsBaseline;

    public EditorProjectDocument Load()
    {
        if (!Directory.Exists(_projectPath)) throw new DirectoryNotFoundException($"Project directory not found: {_projectPath}");
        var settings = LoadSettings(_projectPath);
        configurations?.Validate(settings);
        _settingsBaseline = File.ReadAllBytes(new ProjectRawReader(_projectPath).Resolve("settings.json"));
        var loaded = _repository.Load(_projectPath);
        return new EditorProjectDocument { Graph = loaded.Document, GroupEntries = loaded.GroupEntries, Configuration = settings };
    }

    public Task SaveAsync(EditorProjectDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(Path.Combine(_projectPath, "settings.json"))) _ = LoadSettings(_projectPath);
        var plan = PrepareSave(document);
        _repository.ApplySave(_projectPath, plan, cancellationToken);
        _settingsBaseline = plan.Writes["settings.json"].ToArray();
        return Task.CompletedTask;
    }

    public ProjectSavePlan PrepareSave(EditorProjectDocument document)
    {
        configurations?.Validate(document.Configuration);
        var settingsPath = new ProjectRawReader(_projectPath).Resolve("settings.json");
        if (_settingsBaseline is not null && (!File.Exists(settingsPath) || !_settingsBaseline.AsSpan().SequenceEqual(File.ReadAllBytes(settingsPath))))
            throw new IOException("Project settings changed after loading. Reload before saving.");
        if (File.Exists(settingsPath)) _ = LoadSettings(_projectPath);
        var plan = _repository.PrepareSave(_projectPath, document.Graph,
            document.GroupEntries.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<EditorEntryData>)pair.Value));
        var settings = document.Configuration.Clone();
        var writes = new Dictionary<string, byte[]>(plan.Writes)
        { ["settings.json"] = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions) };
        return new(writes, plan.DeletedFiles);
    }

    public Task PublishStructureAsync(EditorProjectDocument document, Func<EditorProjectDocument, EditorProjectDocument, GalNet.Editor.Abstraction.Changes.IProjectStructureChange> prepare, CancellationToken cancellationToken = default)
    {
        var change = prepare(document, document);
        var next = EditorDocumentCloner.Clone(document);
        change.Apply(next, GalNet.Editor.Abstraction.Changes.AuthorDocumentVersion.Working);
        var plan = PrepareSave(next);
        if (change is GalNet.Editor.Abstraction.Changes.IProjectFileStructureChange files) plan = plan with { Files = files.Files };
        _repository.ApplySave(_projectPath, plan, cancellationToken);
        _settingsBaseline = plan.Writes["settings.json"].ToArray();
        document.Graph = next.Graph; document.GroupEntries = next.GroupEntries; document.Configuration = next.Configuration;
        return Task.CompletedTask;
    }

    public static ProjectConfiguration LoadSettings(string projectPath)
    {
        var path = new ProjectRawReader(projectPath).Resolve("settings.json");
        using var raw = JsonDocument.Parse(File.ReadAllBytes(path));
        if (raw.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Project settings must be an object.");
        var diagnostics = new List<GalNet.Editor.Abstraction.Commands.ToolDiagnostic>();
        ProjectValidationService.DuplicateProperties(raw.RootElement, "settings.json", "", diagnostics);
        if (diagnostics.Count != 0) throw new InvalidDataException(string.Join("\n", diagnostics.Select(item => $"{item.JsonPointer}: {item.Message}")));
        var settings = raw.RootElement.Deserialize<ProjectConfiguration>(JsonOptions)
            ?? throw new InvalidDataException("Project settings must be an object.");
        return settings;
    }
}
