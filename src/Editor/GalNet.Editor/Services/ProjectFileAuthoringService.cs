using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

/// <summary>One project history entry for each complete file intent.</summary>
public sealed class ProjectFileAuthoringService(IProjectService projects, IProjectContentSession content, EditorHistories histories, IEntryCatalog catalog, IResourceTypeCatalog types, Interfaces.IAssetCatalogService assets) : IProjectFileCommandExecutor
{
    public async Task ExecuteAsync(ProjectFileRequest request)
    {
        var project = projects.Current ?? throw new InvalidOperationException("No project is open.");
        await content.CommitDraftsAsync();
        var operation = new ProjectFileOperation(project.RootPath, request, catalog, types);
        try { await histories.Content.ExecuteAsync(new FileEdit(operation, content, assets)); }
        catch { operation.Dispose(); throw; }
    }
    public async Task<FileCommandResult> ExecuteAsync(string projectPath, IProjectFileCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (projects.Current is not { } project || !ProjectFilePaths.Comparer.Equals(Path.GetFullPath(projectPath), Path.GetFullPath(project.RootPath))) throw new InvalidOperationException("File commands require the active project.");
            await ExecuteAsync(AssetFileCommandExecutor.Translate(command));
            return new(true, null, command.CommandId, [], []);
        }
        catch (Exception failure)
        {
            content.ReportError(failure);
            return FileCommandResult.Failure(EditorDiagnostic.Error(failure is EditorOperationException coded ? coded.Code : "files.failed", failure.Message)) with { Error = failure };
        }
    }
    private sealed record FileEdit(ProjectFileOperation Operation, IProjectContentSession Content, Interfaces.IAssetCatalogService Assets) : IAsyncUndoableEdit, IDisposable
    {
        public string Description => "Project file " + Operation.Action;
        public EditorText DisplayText => new("History.File" + Operation.Action, Operation.TargetLabel);
        public long RetainedBytes => Operation.RetainedBytes;
        public ValueTask RedoAsync() => ApplyAsync(true);
        public ValueTask UndoAsync() => ApplyAsync(false);
        private async ValueTask ApplyAsync(bool forward)
        {
            await Operation.ApplyAsync(forward, Content.PublishStructureAsync);
            try { await Assets.RefreshAsync(); }
            catch (Exception failure) { Content.ReportError(failure); Serilog.Log.Error(failure, "File publication succeeded but asset index refresh failed"); }
        }
        public void Dispose() => Operation.Dispose();
    }
}
