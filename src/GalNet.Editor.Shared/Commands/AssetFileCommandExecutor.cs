using System.Text.Json;
using GalNet.Editor.Abstraction.Commands;

namespace GalNet.Editor.Shared.Commands;

/// <summary>Sandboxed filesystem dispatcher for project asset commands.</summary>
public sealed class AssetFileCommandExecutor : IProjectFileCommandExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<FileCommandResult> ExecuteAsync(
        string projectPath,
        IProjectFileCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var root = Path.GetFullPath(Path.Combine(projectPath, "Assets"));
            Directory.CreateDirectory(root);
            return command switch
            {
                ImportAssetsCommand value => await ImportAsync(projectPath, root, value, cancellationToken),
                CreateAssetDirectoryCommand value => CreateDirectory(root, value),
                MoveAssetCommand value => await MoveAsync(root, value, cancellationToken),
                RenameAssetCommand value => await RenameAsync(root, value, cancellationToken),
                DeleteAssetCommand value => Delete(projectPath, root, value),
                PatchAssetMetadataCommand value => await PatchMetadataAsync(root, value, cancellationToken),
                _ => FileCommandResult.Failure(EditorDiagnostic.Error("asset.command.unsupported", $"Unsupported asset command '{command.CommandId}'."))
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            return FileCommandResult.Failure(EditorDiagnostic.Error("asset.command.failed", exception.Message));
        }
    }

    private static async Task<FileCommandResult> ImportAsync(
        string projectPath,
        string root,
        ImportAssetsCommand command,
        CancellationToken cancellationToken)
    {
        if (command.SourcePaths.Count == 0)
            return FileCommandResult.Failure(EditorDiagnostic.Error("asset.import.empty", "At least one source file is required."));
        var target = Resolve(root, command.TargetDirectory);
        Directory.CreateDirectory(target);
        var sources = command.SourcePaths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (sources.Any(path => !File.Exists(path)))
            return FileCommandResult.Failure(EditorDiagnostic.Error("asset.import.sourceMissing", "One or more import source files do not exist."));

        var destinations = sources.Select(path => Path.Combine(target, Path.GetFileName(path))).ToList();
        if (destinations.Distinct(StringComparer.OrdinalIgnoreCase).Count() != destinations.Count
            || destinations.Any(path => File.Exists(path) || Directory.Exists(path)))
            return FileCommandResult.Failure(EditorDiagnostic.Error("asset.import.collision", "An imported file would collide with an existing asset."));

        var transactionId = $"filetx_{Guid.NewGuid():N}";
        var staging = Path.Combine(projectPath, ".editor-tx", transactionId);
        var moved = new List<string>();
        try
        {
            Directory.CreateDirectory(staging);
            for (var index = 0; index < sources.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var staged = Path.Combine(staging, index.ToString("D4") + Path.GetExtension(sources[index]));
                File.Copy(sources[index], staged, false);
                File.Move(staged, destinations[index]);
                moved.Add(destinations[index]);
                await EnsureMetadataAsync(root, destinations[index], cancellationToken);
            }
            return Success(transactionId, $"Imported {moved.Count} asset file(s).", moved.Select(path => Resource(root, path)));
        }
        catch
        {
            foreach (var path in moved)
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
            }
            throw;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    private static FileCommandResult CreateDirectory(string root, CreateAssetDirectoryCommand command)
    {
        ValidateName(command.Name);
        var parent = Resolve(root, command.ParentDirectory);
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("The parent asset directory does not exist.");
        var destination = Resolve(root, CombineRelative(command.ParentDirectory, command.Name));
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("An asset already exists at the requested path.");
        Directory.CreateDirectory(destination);
        return Success(NewId(), $"Created asset directory '{command.Name}'.", [Resource(root, destination)]);
    }

    private static async Task<FileCommandResult> MoveAsync(string root, MoveAssetCommand command, CancellationToken cancellationToken)
    {
        var source = ResolveExisting(root, command.RelativePath);
        var targetDirectory = Resolve(root, command.TargetDirectory);
        if (!Directory.Exists(targetDirectory)) throw new DirectoryNotFoundException("The target asset directory does not exist.");
        if (Directory.Exists(source) && IsWithin(source, targetDirectory)) throw new IOException("An asset directory cannot be moved into itself.");
        var destination = Resolve(root, CombineRelative(command.TargetDirectory, Path.GetFileName(source)));
        EnsureDestinationFree(destination);
        MovePath(source, destination);
        await MoveSidecarAndRewriteAsync(root, source, destination, cancellationToken);
        return Success(NewId(), $"Moved asset '{command.RelativePath}' to '{command.TargetDirectory}'.", [Resource(root, source), Resource(root, destination)]);
    }

    private static async Task<FileCommandResult> RenameAsync(string root, RenameAssetCommand command, CancellationToken cancellationToken)
    {
        if (command.UpdateReferences)
            return FileCommandResult.Failure(EditorDiagnostic.Error("asset.rename.referenceUpdateUnsupported", "Automatic reference updates are not yet supported; retry with updateReferences=false and patch references explicitly."));
        ValidateName(command.NewName);
        var source = ResolveExisting(root, command.RelativePath);
        var relativeParent = Path.GetDirectoryName(command.RelativePath.Replace('/', Path.DirectorySeparatorChar)) ?? "";
        var destination = Resolve(root, CombineRelative(relativeParent, command.NewName));
        EnsureDestinationFree(destination);
        MovePath(source, destination);
        await MoveSidecarAndRewriteAsync(root, source, destination, cancellationToken);
        return Success(NewId(), $"Renamed asset '{command.RelativePath}' to '{command.NewName}'.", [Resource(root, source), Resource(root, destination)]);
    }

    private static FileCommandResult Delete(string projectPath, string root, DeleteAssetCommand command)
    {
        var source = ResolveExisting(root, command.RelativePath);
        var references = FindReferences(projectPath, source, command.RelativePath);
        if (references.Count > 0)
            return FileCommandResult.Failure(references
                .Select(resource => EditorDiagnostic.Error("asset.delete.referenced", $"The asset is still referenced by '{resource}'.", resource))
                .ToArray());
        var transactionId = NewId();
        var trashRoot = Path.GetFullPath(Path.Combine(projectPath, ".editor-trash", transactionId));
        Directory.CreateDirectory(trashRoot);
        var destination = Path.Combine(trashRoot, Path.GetFileName(source));
        MovePath(source, destination);
        if (File.Exists(source + ".meta")) File.Move(source + ".meta", destination + ".meta");
        return Success(transactionId, $"Moved asset '{command.RelativePath}' to the project recovery area.", [Resource(root, source)]);
    }

    private static IReadOnlyList<string> FindReferences(string projectPath, string assetPath, string relativePath)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            relativePath.Replace('\\', '/'),
            "Assets/" + relativePath.Replace('\\', '/')
        };
        try
        {
            var metaPath = assetPath + ".meta";
            if (File.Exists(metaPath))
            {
                using var meta = JsonDocument.Parse(File.ReadAllText(metaPath));
                if (meta.RootElement.TryGetProperty("Id", out var id) && !string.IsNullOrWhiteSpace(id.GetString()))
                    values.Add(id.GetString()!);
            }
        }
        catch (JsonException) { }

        var references = new List<string>();
        var document = new DirectProjectPersistence(projectPath).Load();
        foreach (var node in document.Graph.Nodes)
            if (values.Any(value => string.Equals(node.File, value, StringComparison.OrdinalIgnoreCase)))
                references.Add($"graph/nodes/{node.Id}");
        foreach (var (groupId, entries) in document.GroupEntries)
        foreach (var entry in entries)
            if (values.Any(value => entry.Parameters.Values.Any(parameter => parameter.Contains(value, StringComparison.OrdinalIgnoreCase))))
                references.Add($"groups/{groupId}/entries/{entry.StableId}");
        var uiPath = Path.Combine(projectPath, "UI", "ui.json");
        if (File.Exists(uiPath))
        {
            var uiJson = File.ReadAllText(uiPath);
            if (values.Any(value => uiJson.Contains(value, StringComparison.OrdinalIgnoreCase)))
                references.Add("ui");
        }
        return references.Distinct(StringComparer.Ordinal).ToList();
    }

    private static async Task<FileCommandResult> PatchMetadataAsync(string root, PatchAssetMetadataCommand command, CancellationToken cancellationToken)
    {
        var asset = Resolve(root, command.RelativePath);
        if (!File.Exists(asset)) throw new FileNotFoundException("The asset file does not exist.");
        await EnsureMetadataAsync(root, asset, cancellationToken);
        var metaPath = asset + ".meta";
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await File.ReadAllTextAsync(metaPath, cancellationToken)) ?? [];
        if (command.Filter is not null) values["Filter"] = JsonSerializer.SerializeToElement(command.Filter);
        if (command.Compression is not null) values["Compress"] = JsonSerializer.SerializeToElement(command.Compression);
        var temporary = metaPath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(values, JsonOptions), cancellationToken);
        File.Move(temporary, metaPath, true);
        return Success(NewId(), $"Updated metadata for asset '{command.RelativePath}'.", [Resource(root, asset) + ".meta"]);
    }

    private static async Task MoveSidecarAndRewriteAsync(string root, string source, string destination, CancellationToken cancellationToken)
    {
        if (File.Exists(destination))
        {
            if (File.Exists(source + ".meta")) File.Move(source + ".meta", destination + ".meta");
            await EnsureMetadataAsync(root, destination, cancellationToken, rewritePath: true);
            return;
        }
        foreach (var file in Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories).Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
            await EnsureMetadataAsync(root, file, cancellationToken, rewritePath: true);
    }

    private static async Task EnsureMetadataAsync(string root, string asset, CancellationToken cancellationToken, bool rewritePath = false)
    {
        var metaPath = asset + ".meta";
        Dictionary<string, object?> values;
        if (File.Exists(metaPath))
            values = JsonSerializer.Deserialize<Dictionary<string, object?>>(await File.ReadAllTextAsync(metaPath, cancellationToken)) ?? [];
        else
            values = new() { ["Id"] = Guid.NewGuid().ToString("N"), ["Type"] = InferType(asset) };
        if (rewritePath || !values.ContainsKey("Path")) values["Path"] = Path.GetRelativePath(root, asset).Replace('\\', '/');
        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(values, JsonOptions), cancellationToken);
    }

    private static string Resolve(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new UnauthorizedAccessException("Asset paths must be relative to the Assets directory.");
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!string.Equals(full, root, StringComparison.OrdinalIgnoreCase) && !IsWithin(root, full))
            throw new UnauthorizedAccessException("Asset path escapes the project Assets directory.");
        return full;
    }

    private static string ResolveExisting(string root, string relative)
    {
        var path = Resolve(root, relative);
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException($"Asset '{relative}' does not exist.");
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("The Assets root cannot be modified as an asset.");
        return path;
    }

    private static bool IsWithin(string parent, string candidate) =>
        candidate.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static string CombineRelative(string parent, string name) => Path.Combine(parent.Replace('/', Path.DirectorySeparatorChar), name);
    private static string Resource(string root, string path) => "assets/" + Path.GetRelativePath(root, path).Replace('\\', '/');
    private static string NewId() => $"filetx_{Guid.NewGuid():N}";
    private static void EnsureDestinationFree(string path) { if (File.Exists(path) || Directory.Exists(path)) throw new IOException("An asset already exists at the destination."); }
    private static void MovePath(string source, string destination) { if (Directory.Exists(source)) Directory.Move(source, destination); else File.Move(source, destination); }
    private static void ValidateName(string name) { if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or "..") throw new ArgumentException("Invalid asset name."); }
    private static string InferType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif" => "sprite",
        ".mp3" or ".wav" or ".ogg" or ".flac" or ".m4a" => "audio",
        ".mp4" or ".webm" or ".mkv" or ".avi" or ".mov" => "video",
        _ => "unknown"
    };
    private static FileCommandResult Success(string transactionId, string description, IEnumerable<string> resources) =>
        new(true, transactionId, description, resources.Distinct(StringComparer.Ordinal).ToList(), []);
}
