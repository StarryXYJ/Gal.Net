using System.Security.Cryptography;
using System.Text.Json;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Shared.Commands;

public sealed class ProjectRepairRequiredException(string recoveryDirectory, Exception innerException)
    : IOException($"Project repair is required. Recovery material: '{recoveryDirectory}'.", innerException)
{
    public string RecoveryDirectory { get; } = recoveryDirectory;
}

public static class ProjectAuthorFileTransaction
{
    public static void Commit(string projectPath, IReadOnlyDictionary<string, byte[]> writes,
        IReadOnlyList<string> deletedFiles, CancellationToken cancellationToken = default) =>
        new AuthorFileTransaction().Commit(projectPath, writes, deletedFiles, cancellationToken);

    public static void Recover(string projectPath) => new AuthorFileTransaction().Recover(projectPath);
}

/// <summary>Persistent prepare manifest and backups; publication is compensated before reporting failure.</summary>
public class AuthorFileTransaction
{
    private const string TransactionRoot = ".galnet/editor/transactions";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public void Commit(string projectPath, IReadOnlyDictionary<string, byte[]> writes,
        IReadOnlyList<string> deletedFiles, CancellationToken cancellationToken = default)
    {
        var reader = new ProjectRawReader(projectPath);
        var paths = writes.Keys.Concat(deletedFiles).ToArray();
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (paths.Distinct(comparer).Count() != paths.Length) throw new ArgumentException("A file can only be changed once.");
        foreach (var file in paths) ValidatePath(reader, file);
        cancellationToken.ThrowIfCancellationRequested();
        var directory = reader.Resolve(TransactionRoot + "/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var manifest = new AuthorTransactionManifest();
        var publicationStarted = false;
        var keep = false;
        try
        {
            foreach (var file in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = reader.Resolve(file);
                var original = File.Exists(path) ? File.ReadAllBytes(path) : null;
                var next = writes.GetValueOrDefault(file);
                var index = manifest.Files.Count;
                if (original is not null) File.WriteAllBytes(Path.Combine(directory, $"{index}.old"), original);
                if (next is not null) File.WriteAllBytes(Path.Combine(directory, $"{index}.new"), next);
                manifest.Files.Add(new(file, Hash(original), Hash(next)));
            }
            manifest.Status = "prepared";
            WriteManifest(directory, manifest);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var file in manifest.Files)
                if (ReadHash(reader.Resolve(file.Path)) != file.BeforeHash)
                    throw new IOException($"Author file changed before publication: '{file.Path}'.");
            manifest.Status = "publishing";
            WriteManifest(directory, manifest);
            publicationStarted = true;
            for (var index = 0; index < manifest.Files.Count; index++)
            {
                var file = manifest.Files[index];
                Publish(reader.Resolve(file.Path), file.AfterHash is null ? null : Path.Combine(directory, $"{index}.new"));
            }
            manifest.Status = "committed";
            WriteManifest(directory, manifest);
        }
        catch (Exception failure)
        {
            if (publicationStarted)
            {
                try { RestoreAll(reader, directory, manifest); }
                catch (Exception compensation)
                {
                    keep = true;
                    manifest.Status = "repair-required";
                    try { WriteManifest(directory, manifest); }
                    catch (Exception manifestFailure) { compensation = new AggregateException(compensation, manifestFailure); }
                    throw new ProjectRepairRequiredException(directory, new AggregateException(failure, compensation));
                }
            }
            throw;
        }
        finally
        {
            if (!keep && Directory.Exists(directory))
                try { Directory.Delete(directory, recursive: true); }
                catch (IOException cleanup) { Serilog.Log.Warning(cleanup, "Transaction cleanup deferred: {Directory}", directory); }
                catch (UnauthorizedAccessException cleanup) { Serilog.Log.Warning(cleanup, "Transaction cleanup deferred: {Directory}", directory); }
        }
    }

    protected virtual void Publish(string path, string? stagedFile) => Replace(path, stagedFile);
    protected virtual void Restore(string path, string? backupFile) => Replace(path, backupFile);

    private static void Replace(string path, string? source)
    {
        if (source is null) { File.Delete(path); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.Copy(source, temporary); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void RestoreAll(ProjectRawReader reader, string directory, AuthorTransactionManifest manifest)
    {
        var failures = new List<Exception>();
        foreach (var file in manifest.Files)
        {
            ValidatePath(reader, file.Path);
            var current = ReadHash(reader.Resolve(file.Path));
            if (current != file.BeforeHash && current != file.AfterHash)
                throw new IOException($"Recovery found external content: '{file.Path}'.");
        }
        for (var index = manifest.Files.Count - 1; index >= 0; index--)
        {
            var file = manifest.Files[index];
            try
            {
                if (ReadHash(reader.Resolve(file.Path)) == file.BeforeHash) continue;
                var backup = file.BeforeHash is null ? null : Path.Combine(directory, $"{index}.old");
                if (backup is not null && ReadHash(backup) != file.BeforeHash)
                    throw new IOException($"Recovery backup is missing or corrupt: '{file.Path}'.");
                Restore(reader.Resolve(file.Path), backup);
            }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count > 0) throw new AggregateException("Author-file recovery failed.", failures);
    }

    public void Recover(string projectPath)
    {
        var reader = new ProjectRawReader(projectPath);
        var root = reader.Resolve(TransactionRoot);
        if (!Directory.Exists(root)) return;
        foreach (var candidate in Directory.EnumerateDirectories(root))
        {
            var directory = reader.Resolve(reader.Relative(candidate));
            var manifestPath = reader.Resolve(reader.Relative(Path.Combine(directory, "manifest.json")));
            if (!File.Exists(manifestPath)) { Directory.Delete(directory, recursive: true); continue; }
            try
            {
                var manifest = JsonSerializer.Deserialize<AuthorTransactionManifest>(File.ReadAllBytes(manifestPath), JsonOptions)
                    ?? throw new InvalidDataException("Recovery manifest is empty.");
                if (manifest.Status is "publishing" or "repair-required") RestoreAll(reader, directory, manifest);
                else if (manifest.Status is not ("prepared" or "committed")) throw new InvalidDataException("Unknown transaction status.");
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception failure) { throw new ProjectRepairRequiredException(directory, failure); }
        }
    }

    private static void ValidatePath(ProjectRawReader reader, string file)
    {
        var path = reader.Resolve(file);
        if (reader.Relative(path) != file ||
            file != "settings.json" && file != "Graph/graph.json" && file != ".galnet/editor-state.json" &&
            !(file.StartsWith("Graph/", StringComparison.Ordinal) && file.EndsWith(".rawgalgroup", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Not a normalized author document path: '{file}'.");
        if (Directory.Exists(path)) throw new IOException($"A directory occupies author file '{file}'.");
    }

    private static string? Hash(byte[]? bytes) => bytes is null ? null : Convert.ToHexString(SHA256.HashData(bytes));
    private static string? ReadHash(string path) => File.Exists(path) ? Hash(File.ReadAllBytes(path)) : null;
    private static void WriteManifest(string directory, AuthorTransactionManifest manifest)
    {
        var path = Path.Combine(directory, "manifest.json");
        var next = path + ".tmp";
        File.WriteAllBytes(next, JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions));
        File.Move(next, path, overwrite: true);
    }
}

public sealed class AuthorTransactionManifest
{
    public string Status { get; set; } = "prepared";
    public List<AuthorTransactionFile> Files { get; set; } = [];
}

public sealed record AuthorTransactionFile(string Path, string? BeforeHash, string? AfterHash);
