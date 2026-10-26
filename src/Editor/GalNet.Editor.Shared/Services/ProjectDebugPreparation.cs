using System.Diagnostics;
using GalNet.Core.Entry;
using GalNet.Debugging;
using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.Shared.Services;

/// <summary>Prepares compiled content and its matching source maps without depending on a panel or workspace.</summary>
public sealed class ProjectDebugPreparation(IProjectService projects, IEntryCatalog catalog, ProjectSchemaSession schemas,
    ExternalGameDebugClient client) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    public async Task<ProjectContentBuildResult> PrepareAsync(Action<string>? reportStage = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var token = _lifetime.Token;
        await client.DisconnectAsync();
        var project = projects.Current ?? throw new InvalidOperationException("No project.");
        client.BeginDiagnostics(Path.Combine(project.RootPath, ".galnet", "debug-profile"));
        var timer = Stopwatch.StartNew();
        reportStage?.Invoke("Debug.SavingProject");
        client.RecordDiagnostic("Saving project changes before building test content.");
        await projects.SaveAsync();
        RequireCurrent();
        client.RecordDiagnostic($"Project save completed in {timer.ElapsedMilliseconds} ms.");
        timer.Restart();
        reportStage?.Invoke("Debug.BuildingContent");
        var output = Path.Combine(project.RootPath, "Output", "Debug");
        client.RecordDiagnostic($"Building test content at {output}.");
        var result = await Task.Run(() => new ProjectContentBuilder(catalog).BuildAsync(project.RootPath, output, token), token);
        RequireCurrent();
        client.RecordDiagnostic($"Test content and author source map built in {timer.ElapsedMilliseconds} ms for {result.CompiledGroupCount} Group(s).");
        client.Listen(new(project.Id, DebugContentIdentity.Compute(result.OutputPath), schemas.Context.Fingerprint));
        return result;

        void RequireCurrent()
        {
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(projects.Current, project)) throw new OperationCanceledException("The debug project was closed.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
