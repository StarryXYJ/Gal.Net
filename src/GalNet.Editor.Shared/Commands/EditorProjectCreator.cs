using System.Text.Json;
using GalNet.Core.Settings;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.Shared.UI;

namespace GalNet.Editor.Shared.Commands;

public static class EditorProjectCreator
{
    public static async Task CreateAsync(
        string projectPath,
        string? projectName = null,
        CancellationToken cancellationToken = default)
    {
        projectPath = Path.GetFullPath(projectPath);
        projectName = string.IsNullOrWhiteSpace(projectName)
            ? Path.GetFileName(Path.TrimEndingDirectorySeparator(projectPath))
            : projectName.Trim();
        if (Directory.Exists(projectPath) && Directory.EnumerateFileSystemEntries(projectPath).Any())
            throw new IOException($"Project directory is not empty: {projectPath}");

        foreach (var directory in new[]
                 {
                     "Graph/groups", "Assets/Layer", "Assets/Audio", "Assets/Video",
                     "I18n", "Output", "Temp", ".galnet", "UI"
                 })
            Directory.CreateDirectory(Path.Combine(projectPath, directory.Replace('/', Path.DirectorySeparatorChar)));

        var entryId = Guid.NewGuid().ToString("N");
        var groupId = Guid.NewGuid().ToString("N");
        var settings = new ProjectSettings();
        var document = new EditorProjectDocument
        {
            Settings = settings,
            Graph = new EditorGraphDocument
            {
                Name = projectName,
                RootNodeId = entryId,
                Nodes =
                [
                    new EditorGraphNodeDto { Id = entryId, Type = "Entry", Name = "Entry", X = 4620, Y = 4950 },
                    new EditorGraphNodeDto { Id = groupId, Type = "Group", Name = "Opening", X = 4900, Y = 4950, File = $"groups/{groupId}.galgroup" }
                ],
                Edges =
                [
                    new EditorGraphEdgeDto
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        FromNodeId = entryId,
                        FromOutlet = 0,
                        ToNodeId = groupId
                    }
                ]
            },
            GroupEntries =
            {
                [groupId] =
                [
                    new EditorEntryData
                    {
                        StableId = Guid.NewGuid().ToString("N"),
                        Id = 1,
                        Type = "text",
                        Parameters = "speaker=Alice; text=Hello GalNet"
                    }
                ]
            }
        };

        var repository = new EditorDocumentRepository();
        await new FileEditorSessionPersistence(projectPath, repository).SaveAsync(document, cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(projectPath, ".galnet", "editor-state.json"),
            JsonSerializer.Serialize(new GalNet.Editor.Abstraction.Project.EditorProjectState(), new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
        await new FileUiProjectProvider(projectPath, UiProjectDefaults.Create()).SaveAsync(cancellationToken);
    }
}
