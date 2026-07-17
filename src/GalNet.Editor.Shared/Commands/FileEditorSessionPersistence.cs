using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Settings;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Abstraction.Sessions;
using GalNet.Editor.Shared.UI;

namespace GalNet.Editor.Shared.Commands;

public sealed class FileEditorSessionPersistence : IEditorSessionPersistence
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _projectPath;
    private readonly IEditorDocumentRepository _repository;

    public FileEditorSessionPersistence(string projectPath, IEditorDocumentRepository repository)
    {
        _projectPath = Path.GetFullPath(projectPath);
        _repository = repository;
    }

    public async Task SaveAsync(EditorProjectDocument document, CancellationToken cancellationToken = default)
    {
        document.Settings.PlayerVariables = document.Graph.PlayerVariables.Select(item => item.Clone()).ToList();
        document.Settings.SaveVariables = document.Graph.SaveVariables.Select(item => item.Clone()).ToList();
        _repository.Save(
            _projectPath,
            document.Graph,
            document.GroupEntries.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<EditorEntryData>)pair.Value));

        var settingsPath = Path.Combine(_projectPath, "settings.json");
        var temporaryPath = settingsPath + ".tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            JsonSerializer.Serialize(document.Settings, JsonOptions),
            cancellationToken);
        File.Move(temporaryPath, settingsPath, true);

        var uiProvider = new FileUiProjectProvider(_projectPath);
        uiProvider.Replace(document.UiProject);
        await uiProvider.SaveAsync(cancellationToken);
    }

    public static ProjectSettings LoadSettings(string projectPath)
    {
        var settingsPath = Path.Combine(projectPath, "settings.json");
        if (!File.Exists(settingsPath)) return new ProjectSettings();
        return JsonSerializer.Deserialize<ProjectSettings>(File.ReadAllText(settingsPath), JsonOptions) ?? new ProjectSettings();
    }
}
