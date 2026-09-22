using System.Text.Json;
using GalNet.Core.Settings;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;
using GalNet.Core.Entry;

namespace GalNet.Editor.Shared.Commands;

public static class EditorProjectCreator
{
    public static async Task CreateAsync(
        string projectPath,
        IEntryCatalog catalog,
        string? projectName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        projectPath = Path.GetFullPath(projectPath);
        projectName = string.IsNullOrWhiteSpace(projectName)
            ? Path.GetFileName(Path.TrimEndingDirectorySeparator(projectPath))
            : projectName.Trim();
        if (Directory.Exists(projectPath) && Directory.EnumerateFileSystemEntries(projectPath).Any())
            throw new IOException($"Project directory is not empty: {projectPath}");

        foreach (var directory in new[]
                 {
                     "Graph/groups", "Assets/Layer", "Assets/Audio", "Assets/Video",
                     "I18n", "Output", "Temp", ".galnet"
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
                    new EditorGraphNodeDto { Id = groupId, Type = "Group", Name = "Opening", X = 4900, Y = 4950, File = $"groups/{groupId}.rawgalgroup" }
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
            GroupEntries = { [groupId] = [] }
        };

        var repository = new EditorDocumentRepository(catalog);
        await new DirectProjectPersistence(projectPath, repository).SaveAsync(document, cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(projectPath, ".galnet", "editor-state.json"),
            JsonSerializer.Serialize(new GalNet.Editor.Abstraction.Project.EditorProjectState(), new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }
}
