using System.Text.Json;
using GalNet.Core.Compilation;
using GalNet.Editor.Abstraction.Commands;

namespace GalNet.Editor.Shared.Services;

/// <summary>Compiles one source with the project contract and publishes only its compiled file.</summary>
public sealed class GroupCompileService(string projectDirectory)
{
    private readonly ProjectRawReader _reader = new(projectDirectory);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<ToolResult> CompileAsync(GroupCompileRequest request, CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ToolDiagnostic>();
        GroupCompileReport? report = null;
        string? temporary = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = new ProjectQueryService(_reader.Root).ResolveGroup(request.Selector, diagnostics);
            if (source is null || diagnostics.Any(item => item.Severity == "error")) return Result(null, diagnostics);
            var output = ResolveOutput(request, source, diagnostics);
            if (output is null) return Result(null, diagnostics);
            ProjectSchemaContext schema;
            try { schema = ProjectSchemaContext.Load(_reader.Root); }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            { diagnostics.Add(new("contract.load", "error", exception.Message, "Contracts/" + ProjectSchemaContext.AssemblyFileName)); return Result(null, diagnostics); }
            using (schema)
            {
                var document = new ProjectValidationService(_reader.Root).ValidateGroup(source, diagnostics, out var compiled, schema.Catalog);
                if (document is null) return Result(null, diagnostics);
                cancellationToken.ThrowIfCancellationRequested();
                report = new(source, output, request.DryRun, document.Entries.Count, compiled!.Document.Entries.Count, compiled.SourceMap, schema.Fingerprint);
                if (request.DryRun) return Result(report, diagnostics);
                ValidateOutputPath(output);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                temporary = output + ".compile-" + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(compiled.Document, JsonOptions), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                ValidateOutputPath(output);
                File.Move(temporary, output, overwrite: true);
                temporary = null;
            }
        }
        catch (OperationCanceledException)
        { diagnostics.Add(new("group.compileCancelled", "error", "Compilation was cancelled; the existing output was preserved.")); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        { diagnostics.Add(new("group.compileFailed", "error", exception.Message)); }
        finally { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
        return Result(report, diagnostics);
    }

    private string? ResolveOutput(GroupCompileRequest request, string source, List<ToolDiagnostic> diagnostics)
    {
        var id = request.Selector.Id;
        if (request.Output is null && id is null)
        {
            var graphDiagnostics = new List<ToolDiagnostic>();
            var graph = _reader.Read("Graph/graph.json", graphDiagnostics);
            var owners = ProjectRawReader.Array(graph, "nodes", "Graph/graph.json", graphDiagnostics)
                .Where(node => ProjectRawReader.Text(node, "type") == "Group")
                .Where(node => SourceMatches(node, source)).Select(node => ProjectRawReader.Text(node, "id")).ToArray();
            if (graphDiagnostics.Count == 0 && owners.Length == 1 && !string.IsNullOrWhiteSpace(owners[0])) id = owners[0];
            else
            {
                diagnostics.Add(new("group.outputRequired", "error", "A file without a unique Graph Group owner requires explicit --output.", source));
                return null;
            }
        }
        if (request.Output is null && (string.IsNullOrWhiteSpace(id) || id is "." or ".." || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains('/') || id.Contains('\\')))
        { diagnostics.Add(new("group.outputIdInvalid", "error", "Group ID is not safe as an output filename; specify --output.", source, id)); return null; }
        var output = Path.GetFullPath(request.Output ?? $"Output/Graph/groups/{id}.galgroup", _reader.Root);
        ValidateOutputPath(output);
        return output;
    }

    private bool SourceMatches(JsonElement node, string source)
    {
        var file = ProjectRawReader.Text(node, "file");
        if (string.IsNullOrWhiteSpace(file) || Path.IsPathRooted(file)) return false;
        try { return _reader.Resolve("Graph/" + file).Equals(_reader.Resolve(source), PathComparison); }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException) { return false; }
    }

    private void ValidateOutputPath(string output)
    {
        if (!output.EndsWith(".galgroup", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Compiled output must end in .galgroup.");
        // Within the project, only its output directory can receive compiled artifacts.
        if (IsChild(output, _reader.Root) && !IsChild(output, Path.Combine(_reader.Root, "Output")))
            throw new InvalidDataException("Compiled output inside the project must remain under Output/; author files and Contracts are protected.");
        if (Directory.Exists(output)) throw new InvalidDataException("Compiled output is an existing directory.");
        var current = Path.GetPathRoot(output)!;
        foreach (var component in output[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Compiled output must not traverse filesystem links.");
        }
    }
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static bool IsChild(string path, string parent) => path.Equals(parent, PathComparison) || path.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, PathComparison);
    private static ToolResult Result(GroupCompileReport? report, IReadOnlyList<ToolDiagnostic> diagnostics) =>
        new("group.compile", !diagnostics.Any(item => item.Severity == "error"), report, diagnostics);
}
