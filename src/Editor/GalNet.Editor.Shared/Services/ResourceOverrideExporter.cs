using GalNet.Assets;
using GalNet.Assets.Provider;
using GalNet.Core.Assets;
using GalNet.Core.Configuration;

namespace GalNet.Editor.Shared.Services;

/// <summary>Exports an independent resource PAK without compiling story or requiring a base export.</summary>
public static class ResourceOverrideExporter
{
    public static async Task ExportAsync(string projectId, string projectRoot, ResourceOverrideDefinition definition,
        string targetPath, IResourceTypeCatalog types, CancellationToken ct = default)
    {
        // Capture author edits before the first await. The caller may continue editing its own definition.
        var captured = new ResourceOverrideDefinition { Id = definition.Id, Name = definition.Name, Priority = definition.Priority,
            Mappings = new(definition.Mappings, StringComparer.OrdinalIgnoreCase), IncludeResources = [..definition.IncludeResources],
            DeclaredCompleteResources = [..definition.DeclaredCompleteResources] };
        var source = Path.GetFullPath(Path.Combine(projectRoot, "Assets"));
        var target = Path.GetFullPath(targetPath);
        if (!target.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Resource patch output must end in .pak.", nameof(targetPath));
        if (FileSystemPaths.IsSameOrChild(target, source)) throw new InvalidOperationException("Patch output cannot be inside author Assets.");
        FileSystemPaths.RequireNoLinks(source, "Resource snapshot source cannot traverse links.");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var stage = Path.Combine(Path.GetDirectoryName(target)!, ".override-" + Guid.NewGuid().ToString("N"));
        var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(stage);
            // Reject links and copy the complete author resource tree to a private compilation snapshot.
            Copy(source, stage, ct);
            using var provider = new LocalFileProvider(stage, types);
            using var archive = await provider.OpenArchiveAsync("assets", ct);
            var files = archive.AssetIds.Select(id => archive.GetAsset(id)!).ToArray();
            var bytes = ResourceOverrideBuilder.Build(projectId, captured, files, types, ct);
            await File.WriteAllBytesAsync(temporary, bytes, ct);
            using (var verified = Archive.Deserialize("assets", await File.ReadAllBytesAsync(temporary, ct), types))
                foreach (var id in verified.AssetIds) _ = verified.GetAsset(id)!.ReadAllBytes();
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
        }
    }

    private static void Copy(string source, string destination, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Resource snapshots cannot contain links.");
        Directory.CreateDirectory(destination);
        foreach (var path in Directory.EnumerateFileSystemEntries(source).Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Resource snapshots cannot contain links.");
            var target = Path.Combine(destination, Path.GetFileName(path));
            if (Directory.Exists(path)) Copy(path, target, ct); else File.Copy(path, target);
        }
    }
}
