using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Commands;

namespace GalNet.Editor.Shared.Services;

/// <summary>Stable author snapshots shared by file notifications and explicit reload.</summary>
public static class ExternalProjectSnapshotReader
{
    public static ExternalProjectSnapshot Read(string root, IEnumerable<EditorFileDocument> tracked, IEntryCatalog catalog)
    {
        if (HasPendingTransaction(root)) throw new IOException("Project publication is still in progress.");
        var files = tracked.Select(file => new EditorFileDocument { Path = file.Path, ManagedContent = file.ManagedContent }).ToArray();
        var before = ReadFiles(root, files);
        var contracts = ContractsFingerprint(root);
        var settings = DirectProjectPersistence.LoadSettings(root);
        var repository = new EditorDocumentRepository(catalog, EditorDocumentValidation.Authoring);
        var loaded = repository.Load(root);
        if (loaded.Document.Nodes.Count(node => node.Type == "Entry") != 1 ||
            !loaded.Document.Nodes.Any(node => node.Type == "Entry" && node.Id == loaded.Document.RootNodeId))
            throw new InvalidDataException("The editor graph requires one explicit Entry root node.");
        foreach (var (path, bytes) in before.Where(pair => IsLocalization(pair.Key))) LocalizationJson.Validate(path, bytes);
        if (HasPendingTransaction(root) || !Same(before, ReadFiles(root, files)) || contracts != ContractsFingerprint(root))
            throw new IOException("Project files changed while reading the external version. Retrying after publication.");
        return new(loaded, settings, before, repository.SourceBaseline, contracts);
    }

    public static bool IsCurrent(string root, ExternalProjectSnapshot snapshot, IEnumerable<EditorFileDocument> tracked) =>
        !HasPendingTransaction(root) && snapshot.ContractsFingerprint == ContractsFingerprint(root) && Same(snapshot.Files, ReadFiles(root, tracked));

    public static Dictionary<string, byte[]> ReadFiles(string root, IEnumerable<EditorFileDocument> tracked)
    {
        var reader = new ProjectRawReader(root);
        var paths = new HashSet<string>(ProjectFilePaths.Comparer) { "Graph/graph.json", "settings.json" };
        var diagnostics = new List<ToolDiagnostic>();
        foreach (var directory in new[] { "Graph", "I18n" })
            if (Directory.Exists(reader.Resolve(directory)))
                foreach (var path in reader.Files(directory, diagnostics))
                    if (path.EndsWith(".rawgalgroup", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) paths.Add(path);
        foreach (var file in tracked.Where(file => file.ManagedContent)) paths.Add(file.Path);
        if (diagnostics.Any(item => item.Severity == "error")) throw new IOException(string.Join("\n", diagnostics.Select(item => item.Message)));
        var result = new Dictionary<string, byte[]>(ProjectFilePaths.Comparer);
        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            var full = reader.Resolve(path);
            if (File.Exists(full)) result.Add(path, File.ReadAllBytes(full));
        }
        return result;
    }

    public static bool IsLocalization(string path)
    {
        if (path == LocalizationJson.MetadataPath) return true;
        if (!path.StartsWith("I18n/", StringComparison.Ordinal) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;
        try { return LocalizationJson.Path(path) == path; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException) { return false; }
    }

    public static string ContractsFingerprint(string root)
    {
        var reader = new ProjectRawReader(root);
        if (!Directory.Exists(reader.Resolve("Contracts"))) return "";
        var diagnostics = new List<ToolDiagnostic>();
        var files = reader.Files("Contracts", diagnostics).Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        if (diagnostics.Count != 0) throw new IOException("Cannot inspect project contract dependencies.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(path + "\0"));
            using var stream = File.OpenRead(reader.Resolve(path));
            hash.AppendData(SHA256.HashData(stream));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static bool Same(IReadOnlyDictionary<string, byte[]> first, IReadOnlyDictionary<string, byte[]> second) =>
        first.Count == second.Count && first.All(pair => second.TryGetValue(pair.Key, out var bytes) && pair.Value.AsSpan().SequenceEqual(bytes));

    public static bool HasPendingTransaction(string root)
    {
        var directory = new ProjectRawReader(root).Resolve(".galnet/editor/transactions");
        if (!Directory.Exists(directory)) return false;
        foreach (var transaction in Directory.EnumerateDirectories(directory))
        {
            var manifest = Path.Combine(transaction, "manifest.json");
            try
            {
                if (!File.Exists(manifest)) return true;
                using var json = JsonDocument.Parse(File.ReadAllBytes(manifest));
                if (json.RootElement.GetProperty("status").GetString() != "committed") return true;
            }
            catch (DirectoryNotFoundException) { }
            catch (FileNotFoundException) { }
            catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException or UnauthorizedAccessException) { return true; }
        }
        return false;
    }
}
