using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Shared.Commands;

/// <summary>Identifies a project and coordinates shutdown with a platform recycle operation.</summary>
public sealed class ProjectDeleteService(IProjectRecycleBin recycleBin)
{
    public async Task<ToolResult> DeleteAsync(string path, bool dryRun = false,
        Func<string, Task>? beforeRecycle = null, Action<string>? afterRecycle = null,
        CancellationToken cancellationToken = default)
    {
        ProjectDeleteReport? report = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (root, name) = Identify(path);
            report = new(root, name, dryRun, false);
            if (!recycleBin.IsSupported) throw new PlatformNotSupportedException("This host has no supported project recycle bin.");
            if (dryRun) return new("project.delete", true, report, []);
            if (beforeRecycle is not null) await beforeRecycle(root);
            cancellationToken.ThrowIfCancellationRequested();
            Identify(root); // Recheck after asynchronous host shutdown.
            await recycleBin.RecycleAsync(root, cancellationToken);
            report = report with { Recycled = true };
            afterRecycle?.Invoke(root);
            return new("project.delete", true, report, []);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            return new("project.delete", false, report,
                [new(report?.Recycled == true ? "project.recentUpdateFailed" : "project.recycleRejected", "error", exception.Message)]);
        }
    }

    private static (string Root, string Name) Identify(string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (Path.GetPathRoot(root) == root) throw new InvalidDataException("A filesystem root is not a project.");
        for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Project deletion must not traverse directory links.");
        var reader = new ProjectRawReader(root);
        var diagnostics = new List<ToolDiagnostic>();
        var graph = reader.Read("Graph/graph.json", diagnostics);
        new ProjectValidationService(root).ValidateGraph(graph, diagnostics, structureOnly: true, checkSourceReferences: false);
        if (diagnostics.Any(item => item.Severity == "error")) throw new InvalidDataException("Not a recognized current-format project: " + string.Join("; ", diagnostics.Select(item => item.Message)));
        _ = DirectProjectPersistence.LoadSettings(root);
        return (root, ProjectRawReader.Text(graph!.Value, "name")!);
    }
}
