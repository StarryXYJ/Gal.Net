using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Assets.Variants;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Services;

public sealed partial class ProjectFileOperation
{
    private readonly List<(string From, string To)> _variantCopies = [];
    private readonly HashSet<string> _variantDescriptors = new(ProjectFilePaths.Comparer);

    private (Dictionary<string, string> Files, HashSet<string> Directories) AssetSnapshot()
    {
        var files = new Dictionary<string, string>(ProjectFilePaths.Comparer);
        var directories = new HashSet<string>(ProjectFilePaths.Comparer);
        foreach (var root in new[] { "Assets", "Buildin" }.Select(_reader.Resolve).Where(Directory.Exists))
        {
            var tree = Enumerate(root);
            foreach (var path in tree.Files) files.Add(_reader.Relative(path), path);
            foreach (var path in tree.Directories) directories.Add(_reader.Relative(path));
        }
        return (files, directories);
    }

    private void PrepareVariantRelations()
    {
        if (_request.Action is not (ProjectFileAction.Move or ProjectFileAction.Copy)) return;
        if (!_before.Keys.Concat(_after.Keys).Any(ProjectFilePaths.IsResource)) return;
        var snapshot = AssetSnapshot();
        foreach (var path in _before.Keys) snapshot.Files.Remove(path);
        foreach (var pair in _after.Where(pair => ProjectFilePaths.IsResource(pair.Key))) snapshot.Files[pair.Key] = pair.Value.Backup;
        foreach (var pair in snapshot.Files.Where(pair => IsVariants(pair.Key, snapshot.Files)).ToArray())
        {
            var destination = pair.Key;
            _variantDescriptors.Add(destination);
            var origin = destination;
            (string From, string To)? copied = null;
            foreach (var move in _moves.Where(move => !move.From.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
                if (ProjectFilePaths.Within(destination, move.To)) { origin = move.From + destination[move.To.Length..]; break; }
            foreach (var copy in _variantCopies)
                if (ProjectFilePaths.Within(destination, copy.To)) { origin = copy.From + destination[copy.To.Length..]; copied = copy; break; }
            _variantDescriptors.Add(origin);
            var bytes = File.ReadAllBytes(pair.Value);
            var source = ImageVariantsCodec.ReadSource(bytes);
            var oldFolder = origin[..origin.IndexOf('/')] + "/" + LocalAssetCompiler.ResolveFolder(ProjectFilePaths.ResourceRelative(origin), source.Folder);
            oldFolder = oldFolder.TrimEnd('/');
            var newFolder = Map(oldFolder, true);
            if (copied is { } relation && ProjectFilePaths.Within(oldFolder, relation.From))
                newFolder = relation.To + oldFolder[relation.From.Length..];
            if (origin == destination && oldFolder == newFolder) continue;
            var parent = Path.GetDirectoryName(_reader.Resolve(destination))!;
            var relative = Path.GetRelativePath(parent, _reader.Resolve(newFolder)).Replace('\\', '/');
            var json = JsonNode.Parse(bytes)!.AsObject();
            json["folder"] = relative;
            if (!_after.ContainsKey(destination))
            {
                _before[destination] = SnapshotFile(pair.Value);
            }
            _after[destination] = Store(Encoding.UTF8.GetBytes(json.ToJsonString(new JsonSerializerOptions { WriteIndented = true })));
        }
    }

    private void RebaseVariantBuffer(EditorFileDocument file, bool forward)
    {
        if (!_variantDescriptors.Contains(file.Path)) return;
        var source = ImageVariantsCodec.ReadSource(file.Data);
        var folder = (file.Path[..file.Path.IndexOf('/')] + "/" + LocalAssetCompiler.ResolveFolder(ProjectFilePaths.ResourceRelative(file.Path), source.Folder)).TrimEnd('/');
        var destination = Map(file.Path, forward);
        var mappedFolder = Map(folder, forward);
        if (file.Path == destination && folder == mappedFolder) return;
        var json = JsonNode.Parse(file.Data)!.AsObject();
        json["folder"] = Path.GetRelativePath(Path.GetDirectoryName(_reader.Resolve(destination))!, _reader.Resolve(mappedFolder)).Replace('\\', '/');
        file.Data = Encoding.UTF8.GetBytes(json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private bool IsVariants(string path, IReadOnlyDictionary<string, string> files)
    {
        if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return false;
        if (_types.TryGetByExtension(Path.GetExtension(path), out var type) && type.TypeId == ImageVariantsCodec.ResourceType) return true;
        return files.TryGetValue(path + ".meta", out var sidecar)
            && _codec.Deserialize(File.ReadAllText(sidecar), ProjectFilePaths.ResourceRelative(path)).TypeId == ImageVariantsCodec.ResourceType;
    }

    private void ValidateVariants(IReadOnlyDictionary<string, Snapshot> current, IReadOnlyDictionary<string, Snapshot> desired,
        IReadOnlyCollection<string> currentDirectories, IReadOnlyCollection<string> desiredDirectories, EditorProjectDocument document)
    {
        if (!current.Keys.Concat(desired.Keys).Any(ProjectFilePaths.IsResource)) return;
        var snapshot = AssetSnapshot();
        foreach (var path in current.Keys) snapshot.Files.Remove(path);
        foreach (var pair in desired.Where(pair => ProjectFilePaths.IsResource(pair.Key))) snapshot.Files[pair.Key] = pair.Value.Backup;
        if (!snapshot.Files.Keys.Any(path => IsVariants(path, snapshot.Files))) return;
        snapshot.Directories.ExceptWith(currentDirectories);
        snapshot.Directories.UnionWith(desiredDirectories.Where(path => path is "Assets" or "Buildin" || ProjectFilePaths.IsResource(path)));
        var temporary = new List<string>();
        try
        {
            foreach (var file in document.Files.Values.Where(file => file.ManagedContent && file.Exists
                && ProjectFilePaths.IsResource(file.Path) && IsVariants(file.Path, snapshot.Files)))
            {
                var path = Path.Combine(_retained, "validate-" + Guid.NewGuid().ToString("N"));
                temporary.Add(path);
                File.WriteAllBytes(path, file.Data);
                snapshot.Files[file.Path] = path;
            }
            foreach (var root in new[] { "Assets", "Buildin" })
            {
                var prefix = root + "/";
                var files = snapshot.Files.Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(pair => pair.Key[prefix.Length..], pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                var directories = snapshot.Directories.Where(path => path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .Select(path => path.Equals(root, StringComparison.OrdinalIgnoreCase) ? "" : path[prefix.Length..]).ToArray();
                _ = LocalAssetCompiler.CompileAsync(files, directories, _types).GetAwaiter().GetResult();
            }
        }
        finally { foreach (var path in temporary) File.Delete(path); }
    }
}
