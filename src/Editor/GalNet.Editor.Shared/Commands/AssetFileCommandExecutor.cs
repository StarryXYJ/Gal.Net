using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Shared.Commands;

/// <summary>CLI adapter. The same planner and publisher are used by the GUI; only the GUI retains undo records.</summary>
public sealed class AssetFileCommandExecutor(IEntryCatalog catalog, IResourceTypeCatalog? resourceTypes = null,
    GalNet.Core.Configuration.ProjectConfigurationSchema? configurations = null) : IProjectFileCommandExecutor
{
    public static ProjectFileRequest Translate(IProjectFileCommand command)
    {
        static string Asset(string path) => "Assets/" + path.Trim('/');
        return command switch
        {
            ImportAssetsCommand value => new(ProjectFileAction.Import, value.SourcePaths, Asset(value.TargetDirectory).TrimEnd('/')),
            CreateAssetDirectoryCommand value => new(ProjectFileAction.CreateDirectory, [], Asset(value.ParentDirectory).TrimEnd('/') + "/" + value.Name),
            MoveAssetCommand value => new(ProjectFileAction.Move, [Asset(value.RelativePath)], Asset(value.TargetDirectory).TrimEnd('/')),
            RenameAssetCommand { UpdateReferences: true } => throw new EditorOperationException("files.references", "Unknown references cannot be rewritten automatically.", "custom references"),
            RenameAssetCommand value => new(ProjectFileAction.Move, [Asset(value.RelativePath)], (Path.GetDirectoryName(Asset(value.RelativePath))?.Replace(Path.DirectorySeparatorChar, '/') ?? "Assets") + "/" + value.NewName, false),
            DeleteAssetCommand value => new(ProjectFileAction.Delete, [Asset(value.RelativePath)]),
            PatchAssetMetadataCommand value => new(ProjectFileAction.Metadata, [Asset(value.RelativePath)], Filter: value.Filter, Compression: value.Compression),
            _ => throw new ArgumentException($"Unsupported file command: {command.CommandId}")
        };
    }
    public async Task<FileCommandResult> ExecuteAsync(string projectPath, IProjectFileCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var persistence = new DirectProjectPersistence(projectPath, new EditorDocumentRepository(catalog), configurations);
            var document = persistence.Load();
            using var edit = new ProjectFileOperation(projectPath, Translate(command), catalog, resourceTypes);
            await edit.ApplyAsync(true, prepare => persistence.PublishStructureAsync(document, prepare, cancellationToken));
            return new(true, edit.Id, command.CommandId, [Translate(command).Target], []);
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            return FileCommandResult.Failure(EditorDiagnostic.Error(failure is EditorOperationException coded ? coded.Code : "files.failed", failure.Message)) with { Error = failure };
        }
    }
}
