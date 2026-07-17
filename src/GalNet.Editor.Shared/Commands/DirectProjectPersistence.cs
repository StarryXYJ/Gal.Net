using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Settings;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.Shared.UI;

namespace GalNet.Editor.Shared.Commands;

public sealed class DirectProjectPersistence(string projectPath, IEditorDocumentRepository? repository = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly string _projectPath = Path.GetFullPath(projectPath);
    private readonly IEditorDocumentRepository _repository = repository ?? new EditorDocumentRepository();

    public EditorProjectDocument Load()
    {
        if (!Directory.Exists(_projectPath)) throw new DirectoryNotFoundException($"Project directory not found: {_projectPath}");
        var settings = LoadSettings(_projectPath);
        var loaded = _repository.Load(_projectPath, Path.GetFileName(Path.TrimEndingDirectorySeparator(_projectPath)), settings);
        return new EditorProjectDocument { Graph = loaded.Document, GroupEntries = loaded.GroupEntries, Settings = settings, UiProject = new FileUiProjectProvider(_projectPath).Current };
    }

    public async Task SaveAsync(EditorProjectDocument document, CancellationToken cancellationToken = default)
    {
        document.Settings.PlayerVariables = document.Graph.PlayerVariables.Select(item => item.Clone()).ToList();
        document.Settings.SaveVariables = document.Graph.SaveVariables.Select(item => item.Clone()).ToList();
        _repository.Save(_projectPath, document.Graph, document.GroupEntries.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<EditorEntryData>)pair.Value));
        var settingsPath = Path.Combine(_projectPath, "settings.json");
        var temporaryPath = settingsPath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(document.Settings, JsonOptions), cancellationToken);
        File.Move(temporaryPath, settingsPath, true);
        var uiProvider = new FileUiProjectProvider(_projectPath);
        uiProvider.Replace(document.UiProject);
        await uiProvider.SaveAsync(cancellationToken);
    }

    public static ProjectSettings LoadSettings(string projectPath)
    {
        var path = Path.Combine(projectPath, "settings.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<ProjectSettings>(File.ReadAllText(path), JsonOptions) ?? new ProjectSettings() : new ProjectSettings();
    }
}
