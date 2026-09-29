using System.Text.Json;

namespace GalNet.Editor.Abstraction.Commands;

/// <summary>Project-scoped file command. File commands are transactional but do not enter document Undo history.</summary>
public interface IProjectFileCommand : IEditorCommand;

public sealed record ImportAssetsCommand(IReadOnlyList<string> SourcePaths, string TargetDirectory = "") : IProjectFileCommand
{
    public string CommandId => "asset.import";
}

public sealed record CreateAssetDirectoryCommand(string ParentDirectory, string Name) : IProjectFileCommand
{
    public string CommandId => "asset.directory.create";
}

public sealed record MoveAssetCommand(string RelativePath, string TargetDirectory) : IProjectFileCommand
{
    public string CommandId => "asset.move";
}

public sealed record RenameAssetCommand(string RelativePath, string NewName, bool UpdateReferences = false) : IProjectFileCommand
{
    public string CommandId => "asset.rename";
}

public sealed record DeleteAssetCommand(string RelativePath) : IProjectFileCommand
{
    public string CommandId => "asset.delete";
}

public sealed record PatchAssetMetadataCommand(
    string RelativePath,
    string? Filter = null,
    string? Compression = null) : IProjectFileCommand
{
    public string CommandId => "asset.meta.patch";
}

public interface IProjectFileCommandCatalog
{
    IReadOnlyList<IProjectCommandDefinition> GetAll();
    IProjectFileCommand Deserialize(string commandId, JsonElement payload, JsonSerializerOptions? options = null);
}

public interface IProjectFileCommandExecutor
{
    Task<FileCommandResult> ExecuteAsync(
        string projectPath,
        IProjectFileCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record FileCommandResult(
    bool Success,
    string? TransactionId,
    string? Description,
    IReadOnlyList<string> ChangedResources,
    IReadOnlyList<EditorDiagnostic> Diagnostics)
{
    public static FileCommandResult Failure(params EditorDiagnostic[] diagnostics) =>
        new(false, null, null, [], diagnostics);
}
