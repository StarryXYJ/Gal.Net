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
        IReadOnlyList<string> deletedFiles, CancellationToken cancellationToken = default) =>
        Commit(projectPath, new GalNet.Editor.Abstraction.Services.ProjectSavePlan(writes, deletedFiles), cancellationToken);

    public void Commit(string projectPath, GalNet.Editor.Abstraction.Services.ProjectSavePlan plan, CancellationToken cancellationToken = default)
    {
        var reader = new ProjectRawReader(projectPath);
        var extra = plan.Files ?? new GalNet.Editor.Abstraction.Changes.ProjectFilePlan();
        var writes = new Dictionary<string, byte[]>(extra.Writes, StringComparer.Ordinal);
        foreach (var pair in plan.Writes) writes[pair.Key] = pair.Value;
        var copies = extra.Copies.Where(pair => !writes.ContainsKey(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var deletes = plan.DeletedFiles.Concat(extra.Deletes).Where(path => !writes.ContainsKey(path) && !copies.ContainsKey(path)).Distinct(StringComparer.Ordinal).ToArray();
        var paths = deletes.Concat(writes.Keys).Concat(copies.Keys).ToArray();
        if (paths.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Count() != paths.Length)
            throw new ArgumentException("Case-only path changes must use a case rename plan.");
        foreach (var path in paths) ValidatePath(reader, path);
        foreach (var rename in extra.CaseRenames)
        {
            ValidateLocation(reader, rename.Source); ValidateLocation(reader, rename.Target);
            if (!OperatingSystem.IsWindows() || rename.Source == rename.Target || !rename.Source.Equals(rename.Target, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A case rename must identify the same Windows path.");
        }
        foreach (var pair in extra.ExpectedHashes)
            if (ReadHash(reader.Resolve(pair.Key)) != pair.Value) throw new IOException($"Project file changed before publication: '{pair.Key}'.");
        cancellationToken.ThrowIfCancellationRequested();
        var directory = reader.Resolve(TransactionRoot + "/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var manifest = new AuthorTransactionManifest { CaseRenames = extra.CaseRenames.Select((item, index) => item with { Temporary = reader.Relative(Path.Combine(directory, "case-" + index)) }).ToList() };
        var started = false;
        var keep = false;
        try
        {
            foreach (var file in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = reader.Resolve(file);
                var index = manifest.Files.Count;
                var old = Path.Combine(directory, $"{index}.old");
                var next = Path.Combine(directory, $"{index}.new");
                if (File.Exists(path)) File.Copy(path, old);
                if (writes.TryGetValue(file, out var bytes)) File.WriteAllBytes(next, bytes);
                else if (copies.TryGetValue(file, out var source)) File.Copy(source, next);
                manifest.Files.Add(new(file, ReadHash(old), ReadHash(next)));
            }
            foreach (var pair in extra.ExpectedHashes.Where(pair => !paths.Contains(pair.Key, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)))
                manifest.Files.Add(new(pair.Key, pair.Value, pair.Value, false));
            var desiredDirectories = new HashSet<string>(extra.CreateDirectories, StringComparer.Ordinal);
            foreach (var path in writes.Keys.Concat(copies.Keys).Concat(extra.CreateDirectories))
            {
                var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
                while (!string.IsNullOrEmpty(parent)) { desiredDirectories.Add(parent); parent = Path.GetDirectoryName(parent)?.Replace('\\', '/'); }
            }
            foreach (var path in desiredDirectories.Concat(extra.DeleteDirectories).Distinct(StringComparer.Ordinal))
            {
                ValidateLocation(reader, path);
                if (File.Exists(reader.Resolve(path))) throw new IOException($"A file occupies directory '{path}'.");
                manifest.Directories.Add(new(path, Directory.Exists(reader.Resolve(path)), desiredDirectories.Contains(path)));
            }
            WriteManifest(directory, manifest);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var file in manifest.Files)
                if (ReadHash(reader.Resolve(file.Path)) != file.BeforeHash) throw new IOException($"Project file changed before publication: '{file.Path}'.");
            foreach (var item in manifest.Directories)
                if (Directory.Exists(reader.Resolve(item.Path)) != item.Before) throw new IOException($"Directory changed before publication: '{item.Path}'.");
            manifest.Status = "publishing"; WriteManifest(directory, manifest); started = true;
            foreach (var rename in manifest.CaseRenames) RenameCase(reader, rename.Source, rename.Target, rename.Temporary!);
            foreach (var file in manifest.Files.Where(file => file.Publish && file.AfterHash is null)) Publish(reader.Resolve(file.Path), null);
            foreach (var item in manifest.Directories.Where(item => !item.After).OrderByDescending(item => item.Path.Length))
                if (Directory.Exists(reader.Resolve(item.Path))) Directory.Delete(reader.Resolve(item.Path), false);
            foreach (var item in manifest.Directories.Where(item => item.After).OrderBy(item => item.Path.Length)) Directory.CreateDirectory(reader.Resolve(item.Path));
            for (var index = 0; index < manifest.Files.Count; index++)
                if (manifest.Files[index] is { Publish: true, AfterHash: not null } file) Publish(reader.Resolve(file.Path), Path.Combine(directory, $"{index}.new"));
            manifest.Status = "committed"; WriteManifest(directory, manifest);
        }
        catch (Exception failure)
        {
            if (started)
            {
                try { RestoreAll(reader, directory, manifest); }
                catch (Exception compensation)
                {
                    keep = true; manifest.Status = "repair-required";
                    try { WriteManifest(directory, manifest); } catch (Exception error) { compensation = new AggregateException(compensation, error); }
                    throw new ProjectRepairRequiredException(directory, new AggregateException(failure, compensation));
                }
            }
            throw;
        }
        finally
        {
            if (!keep && Directory.Exists(directory))
                try { Directory.Delete(directory, true); }
                catch (IOException error) { Serilog.Log.Warning(error, "Transaction cleanup deferred: {Directory}", directory); }
                catch (UnauthorizedAccessException error) { Serilog.Log.Warning(error, "Transaction cleanup deferred: {Directory}", directory); }
        }
    }

    private static void RenameCase(ProjectRawReader reader, string source, string target, string temporaryPath)
    {
        ValidateLocation(reader, source); ValidateLocation(reader, target);
        if (!source.Equals(target, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid case rename.");
        var from = reader.Resolve(source); var to = reader.Resolve(target);
        var parent = Path.GetDirectoryName(from)!;
        if (!Directory.EnumerateFileSystemEntries(parent).Any(path => Path.GetFileName(path).Equals(Path.GetFileName(source), StringComparison.Ordinal))) return;
        if (!temporaryPath.StartsWith(TransactionRoot + "/", StringComparison.Ordinal)) throw new InvalidDataException("Invalid case-rename recovery path.");
        var temporary = reader.Resolve(temporaryPath);
        // The intermediate name is covered by the manifest for recovery, including interruption between these moves.
        if (Directory.Exists(from)) { Directory.Move(from, temporary); Directory.Move(temporary, to); }
        else { File.Move(from, temporary); File.Move(temporary, to); }
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
        foreach (var item in manifest.Directories) ValidateLocation(reader, item.Path);
        foreach (var file in manifest.Files) ValidateLocation(reader, file.Path);
        foreach (var rename in manifest.CaseRenames)
        {
            ValidateLocation(reader, rename.Source); ValidateLocation(reader, rename.Target);
            if (!rename.Source.Equals(rename.Target, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid case rename.");
            if (rename.Temporary is null || Path.GetDirectoryName(reader.Resolve(rename.Temporary)) != directory) throw new InvalidDataException("Invalid case-rename recovery path.");
            var temporary = reader.Resolve(rename.Temporary);
            if (Directory.Exists(temporary)) Directory.Move(temporary, reader.Resolve(rename.Source));
            else if (File.Exists(temporary)) File.Move(temporary, reader.Resolve(rename.Source));
        }
        var failures = new List<Exception>();
        foreach (var file in manifest.Files)
        {
            ValidatePath(reader, file.Path);
            var current = ReadHash(reader.Resolve(file.Path));
            if (current != file.BeforeHash && current != file.AfterHash)
                throw new IOException($"Recovery found external content: '{file.Path}'.");
        }
        foreach (var item in manifest.Directories.Where(item => item.Before).OrderBy(item => item.Path.Length)) Directory.CreateDirectory(reader.Resolve(item.Path));
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
        foreach (var item in manifest.Directories.Where(item => !item.Before).OrderByDescending(item => item.Path.Length))
            try { if (Directory.Exists(reader.Resolve(item.Path))) Directory.Delete(reader.Resolve(item.Path), false); } catch (Exception failure) { failures.Add(failure); }
        foreach (var rename in manifest.CaseRenames.AsEnumerable().Reverse())
            try { RenameCase(reader, rename.Target, rename.Source, rename.Temporary!); } catch (Exception failure) { failures.Add(failure); }
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

    private static void ValidateLocation(ProjectRawReader reader, string file)
    {
        var path = reader.Resolve(file);
        var root = file.Split('/')[0];
        if (string.IsNullOrWhiteSpace(file) || reader.Relative(path) != file ||
            new[] { "Contracts", "Output", "Temp", ".git", ".vs", ".idea", ".editor-tx", ".editor-trash", ".galnet" }.Contains(root, StringComparer.OrdinalIgnoreCase) && file != ".galnet/editor-state.json" && file != ".galnet")
            throw new ArgumentException($"Not an editable project path: '{file}'.");
    }
    private static void ValidatePath(ProjectRawReader reader, string file)
    {
        ValidateLocation(reader, file);
        if (file == ".galnet") throw new ArgumentException("The editor internal directory cannot be a file.");
        if (Directory.Exists(reader.Resolve(file))) throw new IOException($"A directory occupies file '{file}'.");
    }
    public static string? ReadHash(string path)
    {
        if (!File.Exists(path)) return null;
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
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
    public List<AuthorTransactionDirectory> Directories { get; set; } = [];
    public List<GalNet.Editor.Abstraction.Changes.ProjectCaseRename> CaseRenames { get; set; } = [];
}

public sealed record AuthorTransactionFile(string Path, string? BeforeHash, string? AfterHash, bool Publish = true);
public sealed record AuthorTransactionDirectory(string Path, bool Before, bool After);
