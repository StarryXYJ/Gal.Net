using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Assets;
using GalNet.Core.Serialization;

namespace GalNet.Storage.FileSystem;

public sealed record GameDistributionResult(string PackagePath, string Sha256, string ProjectId, string BuildId);

/// <summary>Combines immutable application and content inputs without invoking a compiler or loading project code.</summary>
public static class GameDistributionComposer
{
    public const string ManifestPath = "docs/release-manifest.json";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        WriteIndented = true
    };

    public static async Task<GameDistributionResult> ComposeAsync(string runtime, string content, string output,
        CancellationToken cancellationToken = default)
    {
        var runtimeSource = Path.GetFullPath(runtime);
        var contentSource = Path.GetFullPath(content);
        var target = Path.GetFullPath(output);
        if (!target.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The Windows distribution output must be a new .zip file.", nameof(output));
        foreach (var input in new[] { runtimeSource, contentSource })
        {
            FileSystemPaths.RequireNoLinks(input, "Distribution inputs cannot use symbolic links.");
            if (FileSystemPaths.IsSameOrChild(target, input)) throw new InvalidDataException("Output must be outside the input paths.");
        }
        FileSystemPaths.RequireNoLinks(target, "Distribution output cannot use symbolic links.");
        if (File.Exists(target) || Directory.Exists(target)) throw new IOException("Distribution output already exists.");
        var parent = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(parent);
        var stage = Path.Combine(parent, ".compose-" + Guid.NewGuid().ToString("N"));
        var temporary = target + ".compose-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var game = Path.Combine(stage, "game");
            if (Directory.Exists(runtimeSource)) await CopyTreeAsync(runtimeSource, game, cancellationToken);
            else await ExtractRuntimeAsync(runtimeSource, game, cancellationToken);
            var layout = ApplicationPackageLayout.Read(game);
            var manifest = await VerifyApplicationAsync(game, layout, cancellationToken);
            if (layout.RuntimeIdentifier != "win-x64")
                throw new InvalidDataException("compose currently supports win-x64 ZIP distributions; other targets require their platform packager.");
            if (manifest["package"]?.GetValue<string>() != "Sample")
                throw new InvalidDataException("Select a pure Sample runtime package without installed game content.");
            using (var stream = File.OpenRead(FileSystemPaths.ResolveRelativeFile(game, layout.EntryPoint)))
            using (var pe = new PEReader(stream))
                if (pe.PEHeaders.CoffHeader.Machine != Machine.Amd64 || pe.PEHeaders.CorHeader is not null)
                    throw new InvalidDataException("The win-x64 application entry must be an x64 native apphost.");
            var gameContent = FileSystemPaths.ResolveRelativeFile(game, layout.ContentDirectory);
            if (Directory.Exists(gameContent) && Files(gameContent).Any())
                throw new InvalidDataException("The runtime already contains game content.");

            var preparedContent = Path.Combine(stage, "content");
            if (Directory.Exists(contentSource)) await CopyTreeAsync(contentSource, preparedContent, cancellationToken);
            else await GalpakInstaller.InstallAsync(contentSource, preparedContent, cancellationToken);
            var packed = PackedGameDocuments.ReadGame(await File.ReadAllBytesAsync(Path.Combine(preparedContent, PackedGameDocuments.GameFileName), cancellationToken));
            await VerifyContentAsync(preparedContent, packed, cancellationToken);
            VerifyContracts(game, layout, packed.Manifest);
            await CopyTreeAsync(preparedContent, gameContent, cancellationToken);
            manifest["package"] = "Sample-Game";
            manifest["content"] = new JsonObject { ["projectId"] = packed.Manifest.ProjectId, ["buildId"] = packed.Manifest.BuildId };
            manifest["files"] = JsonSerializer.SerializeToNode(await CaptureFilesAsync(game, cancellationToken), Options);
            await File.WriteAllTextAsync(Path.Combine(game, ManifestPath), manifest.ToJsonString(Options), cancellationToken);
            await VerifyApplicationAsync(game, layout, cancellationToken);
            await WriteZipAsync(game, temporary, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await using var archive = File.OpenRead(temporary);
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(archive, cancellationToken));
            await archive.DisposeAsync();
            File.Move(temporary, target);
            return new(target, hash, packed.Manifest.ProjectId, packed.Manifest.BuildId);
        }
        finally
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static async Task<JsonObject> VerifyApplicationAsync(string root, ApplicationPackageLayout layout, CancellationToken ct)
    {
        var manifest = JsonSerializer.Deserialize<JsonObject>(await File.ReadAllBytesAsync(Path.Combine(root, ManifestPath), ct), Options)
            ?? throw new InvalidDataException("Missing release manifest.");
        if (manifest["schemaVersion"]?.GetValue<int>() != 1 || manifest["runtime"]?.GetValue<string>() != layout.RuntimeIdentifier)
            throw new InvalidDataException("Application layout and release manifest disagree.");
        var entries = manifest["files"]?.Deserialize<List<GalpakFileEntry>>(Options)
            ?? throw new InvalidDataException("Missing release file index.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in entries)
        {
            if (!paths.Add(file.Path) || file.Path == ManifestPath) throw new InvalidDataException("Invalid or duplicate release payload path.");
            await VerifyFileAsync(root, file, ct);
        }
        if (!paths.Contains(layout.EntryPoint) || !paths.Contains(ApplicationPackageLayout.FileName)
            || Files(root).Any(path => path != ManifestPath && !paths.Contains(path)))
            throw new InvalidDataException("Application contains missing or undeclared payloads.");
        return manifest;
    }

    private static async Task VerifyContentAsync(string root, PackedGamePackage packed, CancellationToken ct)
    {
        var paths = new HashSet<string>(packed.Installation.Files.Select(file => file.Path), StringComparer.OrdinalIgnoreCase)
        { PackedGameDocuments.GameFileName };
        foreach (var file in packed.Installation.Files) await VerifyFileAsync(root, file, ct);
        foreach (var path in Files(root))
            if (!paths.Contains(path) && !(path.StartsWith("I18n/", StringComparison.Ordinal) && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                && !(path.StartsWith("ResourcePatches/", StringComparison.Ordinal) && path.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)
                    && !path["ResourcePatches/".Length..].Contains('/')))
                throw new InvalidDataException($"Undeclared game content '{path}'.");
    }

    private static void VerifyContracts(string root, ApplicationPackageLayout layout, GameContentManifest content)
    {
        var contracts = content.ContractFiles;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (contracts is null || !new[] { "GalNet.Core.dll", "GalNet.Contracts.Builtins.dll", "GalNet.Project.Contracts.dll" }
            .All(name => contracts.Any(file => file.Path == name)))
            throw new InvalidDataException("Content has no complete contract identity. Export it from a project with deployed Contracts.");
        foreach (var contract in contracts)
        {
            if (contract.Path != Path.GetFileName(contract.Path) || !paths.Add(contract.Path))
                throw new InvalidDataException("Invalid contract identity path.");
            var path = FileSystemPaths.ResolveRelativeFile(root, layout.ContractsDirectory + "/" + contract.Path);
            if (!File.Exists(path) || new FileInfo(path).Length != contract.Size
                || CryptoHelper.HashSHA256(File.ReadAllBytes(path)) != contract.Sha256)
                throw new InvalidDataException($"Runtime contract does not match the exported content: '{contract.Path}'.");
        }
    }

    private static async Task VerifyFileAsync(string root, GalpakFileEntry file, CancellationToken ct)
    {
        var path = FileSystemPaths.ResolveRelativeFile(root, file.Path);
        FileSystemPaths.RequireNoLinks(path, "Payload files cannot use symbolic links.");
        await using var stream = File.OpenRead(path);
        if (file.Size < 0 || file.Sha256 is not { Length: 64 } || stream.Length != file.Size
            || Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct)) != file.Sha256)
            throw new InvalidDataException($"Payload checksum or size mismatch: '{file.Path}'.");
    }

    private static async Task<List<GalpakFileEntry>> CaptureFilesAsync(string root, CancellationToken ct)
    {
        var files = new List<GalpakFileEntry>();
        foreach (var relative in Files(root).Where(path => path != ManifestPath).Order(StringComparer.Ordinal))
        {
            await using var stream = File.OpenRead(FileSystemPaths.ResolveRelativeFile(root, relative));
            files.Add(new(relative, Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct)), stream.Length));
        }
        return files;
    }

    private static IEnumerable<string> Files(string root)
    {
        var directories = new Stack<string>(); directories.Push(root);
        while (directories.TryPop(out var directory))
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Package payloads cannot contain symbolic links.");
                if ((attributes & FileAttributes.Directory) != 0) directories.Push(path);
                else yield return Path.GetRelativePath(root, path).Replace('\\', '/');
            }
    }

    private static async Task CopyTreeAsync(string source, string target, CancellationToken ct)
    {
        FileSystemPaths.RequireNoLinks(source, "Input directories cannot use symbolic links.");
        Directory.CreateDirectory(target);
        foreach (var relative in Files(source))
        {
            ct.ThrowIfCancellationRequested();
            var destination = FileSystemPaths.ResolveRelativeFile(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = File.OpenRead(FileSystemPaths.ResolveRelativeFile(source, relative));
            await using var output = File.Create(destination);
            await input.CopyToAsync(output, ct);
        }
        // Only enumerate directories recursively after Files has rejected every link.
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
    }

    private static async Task ExtractRuntimeAsync(string source, string target, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(source);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(target);
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var relative = entry.FullName.TrimEnd('/');
            var destination = FileSystemPaths.ResolveRelativeFile(target, relative);
            if (!paths.Add(relative) || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000
                || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Runtime archive contains duplicate paths or symbolic links.");
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = entry.Open();
            await using var output = File.Create(destination);
            await input.CopyToAsync(output, ct);
        }
    }

    private static async Task WriteZipAsync(string root, string target, CancellationToken ct)
    {
        using var zip = ZipFile.Open(target, ZipArchiveMode.Create);
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            zip.CreateEntry(Path.GetRelativePath(root, directory).Replace('\\', '/') + "/");
        foreach (var path in Files(root).Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            await using var output = entry.Open();
            await using var input = File.OpenRead(FileSystemPaths.ResolveRelativeFile(root, path));
            await input.CopyToAsync(output, ct);
        }
    }
}
