using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Shared.Commands;

/// <summary>Stages author-file changes and restores published files if later publication fails.
/// Failure recovery within one process, not crash-safe or cross-process atomicity.</summary>
public static class ProjectAuthorFileTransaction
{
    public static void Commit(string projectPath, IReadOnlyDictionary<string, byte[]> writes,
        IReadOnlyList<string> deletedFiles, CancellationToken cancellationToken = default)
    {
        var reader = new ProjectRawReader(projectPath);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var paths = writes.Keys.Concat(deletedFiles).ToArray();
        if (paths.Distinct(comparer).Count() != paths.Length) throw new ArgumentException("A file can only be changed once.");
        var originals = new Dictionary<string, byte[]?>(comparer);
        var temporary = new Dictionary<string, string>(comparer);
        var published = new List<string>();
        try
        {
            foreach (var file in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = reader.Resolve(file);
                if (!string.Equals(reader.Relative(path), file, StringComparison.Ordinal))
                    throw new ArgumentException($"Author file path must be normalized: '{file}'.");
                if (file != "settings.json" && file != "Graph/graph.json" &&
                    !(file.StartsWith("Graph/", StringComparison.Ordinal) && file.EndsWith(".rawgalgroup", StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException($"Not an author document path: '{file}'.");
                if (Directory.Exists(path)) throw new IOException($"A directory occupies author file '{file}'.");
                originals.Add(file, File.Exists(path) ? File.ReadAllBytes(path) : null);
            }
            foreach (var (file, bytes) in writes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = reader.Resolve(file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var stage = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                temporary.Add(file, stage);
                File.WriteAllBytes(stage, bytes);
            }
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var file in paths)
            {
                var path = reader.Resolve(file);
                if (temporary.TryGetValue(file, out var stage)) File.Move(stage, path, overwrite: true);
                else File.Delete(path);
                published.Add(file);
            }
        }
        catch (Exception originalFailure)
        {
            var failures = new List<Exception> { originalFailure };
            foreach (var file in published.AsEnumerable().Reverse())
                try
                {
                    var path = reader.Resolve(file);
                    if (originals[file] is { } bytes)
                    {
                        var stage = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        try { File.WriteAllBytes(stage, bytes); File.Move(stage, path, overwrite: true); }
                        finally { if (File.Exists(stage)) File.Delete(stage); }
                    }
                    else File.Delete(path);
                }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            if (failures.Count != 1) throw new AggregateException("Author-file publication and recovery failed; inspect the project before retrying.", failures);
            throw;
        }
        finally
        {
            foreach (var stage in temporary.Values) if (File.Exists(stage)) File.Delete(stage);
        }
    }
}
