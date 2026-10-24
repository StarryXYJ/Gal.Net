using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Assets.Variants;

namespace GalNet.Editor.Shared.Services;

public sealed partial class ProjectFileOperation
{
    private readonly List<(string From, string To)> _variantCopies = [];

    private (Dictionary<string, string> Files, HashSet<string> Directories) AssetSnapshot()
    {
        var files = new Dictionary<string, string>(ProjectFilePaths.Comparer);
        var directories = new HashSet<string>(ProjectFilePaths.Comparer);
        var root = _reader.Resolve("Assets");
        if (!Directory.Exists(root)) return (files, directories);
        var tree = Enumerate(root);
        foreach (var path in tree.Files) files.Add(_reader.Relative(path), path);
        foreach (var path in tree.Directories) directories.Add(_reader.Relative(path));
        return (files, directories);
    }

    private void PrepareVariantRelations()
    {
        if (_request.Action is not (ProjectFileAction.Move or ProjectFileAction.Copy)) return;
        if (!_before.Keys.Concat(_after.Keys).Any(path => path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))) return;
        var snapshot = AssetSnapshot();
        foreach (var path in _before.Keys) snapshot.Files.Remove(path);
        foreach (var pair in _after.Where(pair => pair.Key.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))) snapshot.Files[pair.Key] = pair.Value.Backup;
        foreach (var pair in snapshot.Files.Where(pair => IsVariants(pair.Key, snapshot.Files)).ToArray())
        {
            var destination = pair.Key;
            var origin = destination;
            (string From, string To)? copied = null;
            foreach (var move in _moves.Where(move => !move.From.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
                if (ProjectFilePaths.Within(destination, move.To)) { origin = move.From + destination[move.To.Length..]; break; }
            foreach (var copy in _variantCopies)
                if (ProjectFilePaths.Within(destination, copy.To)) { origin = copy.From + destination[copy.To.Length..]; copied = copy; break; }
            var bytes = File.ReadAllBytes(pair.Value);
            var source = ImageVariantsCodec.ReadSource(bytes);
            var oldFolder = "Assets/" + LocalAssetCompiler.ResolveFolder(origin[7..], source.Folder);
            oldFolder = oldFolder.TrimEnd('/');
            var newFolder = Map(oldFolder, true);
            if (copied is { } relation && ProjectFilePaths.Within(oldFolder, relation.From))
                newFolder = relation.To + oldFolder[relation.From.Length..];
            if (ProjectFilePaths.Comparer.Equals(origin, destination) && ProjectFilePaths.Comparer.Equals(oldFolder, newFolder)) continue;
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

    private bool IsVariants(string path, IReadOnlyDictionary<string, string> files)
    {
        if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return false;
        if (_types.TryGetByExtension(Path.GetExtension(path), out var type) && type.TypeId == ImageVariantsCodec.ResourceType) return true;
        return files.TryGetValue(path + ".meta", out var sidecar)
            && _codec.Deserialize(File.ReadAllText(sidecar), path[7..]).TypeId == ImageVariantsCodec.ResourceType;
    }

    private void ValidateVariants(IReadOnlyDictionary<string, Snapshot> current, IReadOnlyDictionary<string, Snapshot> desired,
        IReadOnlyCollection<string> currentDirectories, IReadOnlyCollection<string> desiredDirectories)
    {
        if (!current.Keys.Concat(desired.Keys).Any(path => path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))) return;
        var snapshot = AssetSnapshot();
        foreach (var path in current.Keys) snapshot.Files.Remove(path);
        foreach (var pair in desired.Where(pair => pair.Key.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))) snapshot.Files[pair.Key] = pair.Value.Backup;
        if (!snapshot.Files.Keys.Any(path => IsVariants(path, snapshot.Files))) return;
        snapshot.Directories.ExceptWith(currentDirectories);
        snapshot.Directories.UnionWith(desiredDirectories.Where(path => path == "Assets" || path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)));
        var files = snapshot.Files.ToDictionary(pair => pair.Key[7..], pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var directories = snapshot.Directories.Select(path => path == "Assets" ? "" : path[7..]).ToArray();
        _ = LocalAssetCompiler.CompileAsync(files, directories, _types).GetAwaiter().GetResult();
    }
}
