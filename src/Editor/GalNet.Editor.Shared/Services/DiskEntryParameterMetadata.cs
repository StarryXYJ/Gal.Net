using System.Text;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Services;

/// <summary>CLI/build metadata lookup. Missing or ambiguous resources remain unresolved.</summary>
internal static class DiskEntryParameterMetadata
{
    private const int MaximumSourceBytes = 1024 * 1024;
    public static EntryParameterMetadata Capture(string root, EditorProjectDocument document)
    {
        var programs = EntryParameterMetadata.ProgramIds(document).Take(256).ToDictionary(id => id, _ => (ShaderEffectDescriptor?)null, StringComparer.Ordinal);
        if (programs.Count == 0) return new(Programs: programs);
        var requested = programs.Keys.Where(id => Guid.TryParse(id, out _)).GroupBy(id => Guid.Parse(id)).ToDictionary(group => group.Key, group => group.ToArray());
        var reader = new ProjectRawReader(root);
        var diagnostics = new List<ToolDiagnostic>();
        var paths = new Dictionary<Guid, string?>();
        try
        {
            if (!Directory.Exists(reader.Resolve("Assets"))) return new(Programs: programs);
            foreach (var path in reader.Files("Assets", diagnostics).Where(path => path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
            {
                // Do not run the renderer or deserialize arbitrary resource objects.
                if (new FileInfo(reader.Resolve(path)).Length > MaximumSourceBytes) continue;
                var meta = reader.Read(path, diagnostics);
                if (meta is null || !Guid.TryParse(ProjectRawReader.MetadataIdentity(meta.Value).Id, out var id) || !requested.ContainsKey(id)) continue;
                var shader = ProjectRawReader.Text(meta.Value, "type") == "effect-program" ? path[..^5] : null;
                if (!paths.TryAdd(id, shader)) paths[id] = null;
            }
            if (diagnostics.Any(diagnostic => diagnostic.Severity == "error")) return new();
            foreach (var (id, path) in paths)
            {
                if (path is null) continue;
                try
                {
                    var absolute = reader.Resolve(path);
                    if (new FileInfo(absolute).Length > MaximumSourceBytes) continue;
                    var source = File.ReadAllText(absolute, new UTF8Encoding(false, true));
                    foreach (var resource in requested[id])
                    {
                        var parsed = ShaderEffectMetadataParser.Parse(new(resource), source);
                        if (parsed.Success) programs[resource] = parsed.Descriptor;
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
                { /* Unreadable resources retain the normal opaque coverage diagnostic. */ }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        { return new(); }
        return new(Programs: programs);
    }
}
