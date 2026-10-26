using GalNet.Contracts.Builtins.Gallery;
using GalNet.Contracts.Builtins.Assets;
using System.Globalization;
using System.Text.Json;
using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Commands;

namespace GalNet.Editor.Shared.Services;

public enum ProjectFileAction { Move, Copy, Import, Delete, CreateFile, CreateDirectory, Metadata }
public sealed record ProjectFileRequest(ProjectFileAction Action, IReadOnlyList<string> Sources, string Target = "",
    bool TargetIsDirectory = true, byte[]? Content = null, bool DeleteGroups = false, bool AllowCandidateReferences = false,
    string? Filter = null, string? Compression = null, bool UniqueNames = false);
public sealed record ProjectFileReference(string Document, string Field, bool Certain);
public sealed record ProjectFileImpact(IReadOnlyList<string> Groups, IReadOnlyList<ProjectFileReference> References);

/// <summary>Project-local path policy shared by GUI and CLI. Only explicit declared relations are rewritten.</summary>
public static class ProjectFilePaths
{
    public static StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public static bool Within(string path, string parent) => Comparer.Equals(path, parent) || path.StartsWith(parent + "/", OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    public static string Normalize(string path)
    {
        if (Path.IsPathRooted(path)) throw Error("path", path);
        path = path.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(path) || path.Split('/').Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(part.Split('.')[0], StringComparer.OrdinalIgnoreCase))) throw Error("path", path);
        return path;
    }
    public static string Editable(string path)
    {
        path = Normalize(path);
        var first = path.Split('/')[0];
        if (new[] { ".galnet", ".git", ".vs", ".idea", ".editor-tx", ".editor-trash", "Contracts", "Output", "Temp" }.Contains(first, StringComparer.OrdinalIgnoreCase)
            || new[] { "Assets", "Graph", "I18n", "settings.json", "Graph/graph.json", LocalizationJson.MetadataPath }.Contains(path, Comparer)
            || path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) throw Error("protected", path);
        _ = Category(path);
        return path;
    }
    public static string Category(string path)
    {
        if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return "Assets";
        if (path.StartsWith("Graph/", StringComparison.OrdinalIgnoreCase)) return "Graph";
        if (!path.StartsWith("I18n/", StringComparison.OrdinalIgnoreCase)) return "Ordinary";
        var parts = path.Split('/');
        var cultureIndex = parts.Length > 1 && parts[1].Equals("External", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
        if (parts.Length <= cultureIndex + 1 || parts[1].Equals("catalog", StringComparison.OrdinalIgnoreCase)) throw Error("protected", path);
        try { _ = CultureInfo.GetCultureInfo(parts[cultureIndex]); } catch (CultureNotFoundException) { throw Error("category", path); }
        return string.Join('/', parts.Take(cultureIndex + 1));
    }
    public static EditorOperationException Error(string code, string path) => new("files." + code, $"Project file operation rejected ({code}): '{path}'.", path);
}

/// <summary>Reusable data-only edit. Immutable ordinary bytes live in retained files; Group data is rebased on each publication.</summary>
public sealed partial class ProjectFileOperation : IDisposable
{
    private readonly ProjectRawReader _reader;
    private readonly ProjectFileRequest _request;
    private readonly IEntryCatalog _catalog;
    private readonly AssetMetaCodec _codec;
    private readonly IResourceTypeCatalog _types;
    private readonly string _retained;
    private readonly Dictionary<string, Snapshot> _before = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Snapshot> _after = new(StringComparer.Ordinal);
    private readonly HashSet<string> _beforeDirectories = new(StringComparer.Ordinal);
    private readonly HashSet<string> _afterDirectories = new(StringComparer.Ordinal);
    private readonly List<(string From, string To)> _moves = [];
    private readonly List<string> _roots = [];
    private HashSet<string> _managed = new(ProjectFilePaths.Comparer);
    private readonly HashSet<string> _targets = new(ProjectFilePaths.Comparer);
    private long _storedBytes;
    private long _removedBytes;
    private bool _initialized;
    private bool _disposed;
    private FileStream? _lease;
    private bool _caseOnly;
    private HashSet<int>? _galleryIds;
    private RemovedContent? _removedWorking, _removedSaved;
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public long RetainedBytes => _storedBytes + _removedBytes;
    public IReadOnlyList<ProjectFileReference> References { get; private set; } = [];
    public string TargetLabel => _request.Target.Length > 0 ? _request.Target : string.Join(", ", _request.Sources);
    public ProjectFileAction Action => _request.Action;
    public ProjectFileOperation(string root, ProjectFileRequest request, IEntryCatalog catalog, IResourceTypeCatalog? resourceTypes = null)
    {
        _reader = new(root); _request = request; _catalog = catalog; _types = resourceTypes ?? BuiltinResourceTypes.CreateCatalog(); _codec = new(_types);
        _retained = _reader.Resolve(".galnet/editor/history/" + Id);
    }
    public async Task ApplyAsync(bool forward, Func<Func<EditorProjectDocument, EditorProjectDocument, IProjectStructureChange>, Task> publish)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RemovedContent? removedWorking = null, removedSaved = null;
        await publish((working, saved) =>
        {
            if (!_initialized) Initialize(working, saved);
            foreach (var file in working.Files.Values.Concat(saved.Files.Values).Where(file => file.ManagedContent))
            {
                _managed.Add(file.Path); _managed.Add(Map(file.Path, true)); _managed.Add(Map(file.Path, false));
            }
            if (forward && _request.Action == ProjectFileAction.Delete)
            {
                var removed = CaptureRemoved(working);
                if (removed.Document.Graph.Nodes.Count > 0 && !_request.DeleteGroups) throw ProjectFilePaths.Error("referenced", string.Join(", ", removed.Document.Graph.Nodes.Select(node => node.Id)));
                References = FindReferences(working);
                if (References.Any(item => item.Certain || !_request.AllowCandidateReferences)) throw ProjectFilePaths.Error("referenced", string.Join(", ", References.Select(item => item.Document + "/" + item.Field)));
            }
            var current = forward ? _before : _after;
            var desired = forward ? _after : _before;
            var currentDirectories = forward ? _beforeDirectories : _afterDirectories;
            var desiredDirectories = forward ? _afterDirectories : _beforeDirectories;
            foreach (var path in _before.Keys.Concat(_after.Keys).Distinct(ProjectFilePaths.Comparer))
            {
                if (_managed.Contains(path))
                {
                    if (!current.Keys.Contains(path, ProjectFilePaths.Comparer) && File.Exists(_reader.Resolve(path))) throw ProjectFilePaths.Error("changed", path);
                    continue;
                }
                var expected = current.FirstOrDefault(pair => ProjectFilePaths.Comparer.Equals(pair.Key, path)).Value;
                if (AuthorFileTransaction.ReadHash(_reader.Resolve(path)) != expected?.Hash) throw ProjectFilePaths.Error("changed", path);
            }
            foreach (var path in _beforeDirectories.Concat(_afterDirectories).Distinct(ProjectFilePaths.Comparer))
                if (Directory.Exists(_reader.Resolve(path)) != currentDirectories.Contains(path, ProjectFilePaths.Comparer)) throw ProjectFilePaths.Error("changed", path);
            foreach (var root in _roots.Where(root => currentDirectories.Any(path => ProjectFilePaths.Comparer.Equals(path, root))))
            {
                var actual = Enumerate(_reader.Resolve(root)).Files.Select(_reader.Relative).ToHashSet(ProjectFilePaths.Comparer);
                var expected = current.Keys.Where(path => ProjectFilePaths.Within(path, root)).ToHashSet(ProjectFilePaths.Comparer);
                if (!actual.SetEquals(expected)) throw ProjectFilePaths.Error("changed", root);
            }
            var files = new ProjectFilePlan();
            foreach (var pair in current.Where(pair => !_managed.Contains(pair.Key))) files.ExpectedHashes[pair.Key] = pair.Value.Hash;
            foreach (var path in desired.Keys.Where(path => !current.Keys.Contains(path, ProjectFilePaths.Comparer))) files.ExpectedHashes[path] = null;
            if (_caseOnly)
            {
                foreach (var (from, to) in _moves) files.CaseRenames.Add(forward ? new(from, to) : new(to, from));
                foreach (var pair in desired.Where(pair => !_managed.Contains(pair.Key) && (pair.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                    || !current.TryGetValue(pair.Key, out var old) || old.Hash != pair.Value.Hash))) files.Writes[pair.Key] = File.ReadAllBytes(pair.Value.Backup);
            }
            else
            {
                foreach (var pair in desired.Where(pair => !_managed.Contains(pair.Key) && (!current.TryGetValue(pair.Key, out var original) || original.Hash != pair.Value.Hash))) files.Copies[pair.Key] = pair.Value.Backup;
                files.Deletes.AddRange(current.Keys.Where(path => !desired.ContainsKey(path) && !_managed.Contains(path)));
                files.CreateDirectories.AddRange(desiredDirectories);
                files.DeleteDirectories.AddRange(currentDirectories.Where(path => !desiredDirectories.Contains(path)));
            }
            if (_request.Action == ProjectFileAction.Delete && forward)
            {
                removedWorking = CaptureRemoved(working); removedSaved = CaptureRemoved(saved);
            }
            var change = new FileChange(this, forward, files, removedWorking ?? _removedWorking, removedSaved ?? _removedSaved);
            if (current.Keys.Concat(desired.Keys).Any(path => path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)))
                foreach (var (document, version) in new[] { (working, AuthorDocumentVersion.Working), (saved, AuthorDocumentVersion.Saved) })
                {
                    var next = Clone(document);
                    change.Apply(next, version);
                    ValidateVariants(current, desired, currentDirectories, desiredDirectories, next);
                }
            return change;
        });
        if (removedWorking is not null) { _removedWorking = removedWorking; _removedSaved = removedSaved; _removedBytes = JsonSerializer.SerializeToUtf8Bytes(new[] { removedWorking, removedSaved }).LongLength; }
    }

    public ProjectFileImpact Analyze(EditorProjectDocument working, EditorProjectDocument saved)
    {
        if (!_initialized) Initialize(working, saved);
        return new(CaptureRemoved(working).Document.Graph.Nodes.Select(node => node.Id).ToArray(), References);
    }

    private void Initialize(EditorProjectDocument working, EditorProjectDocument saved)
    {
        Directory.CreateDirectory(_retained);
        _lease = new FileStream(Path.Combine(_retained, "owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        _managed = working.Graph.Nodes.Concat(saved.Graph.Nodes).Where(node => node.Type == "Group").Select(node => "Graph/" + node.File).ToHashSet(ProjectFilePaths.Comparer);
        var sources = _request.Action == ProjectFileAction.Import ? _request.Sources.Select(path => Path.GetFullPath(path).Replace(Path.DirectorySeparatorChar, '/')).Distinct(ProjectFilePaths.Comparer).ToArray()
            : _request.Sources.Select(ProjectFilePaths.Editable).Distinct(ProjectFilePaths.Comparer).OrderBy(path => path.Length).ToArray();
        sources = sources.Where(path => !sources.Any(parent => parent != path && ProjectFilePaths.Within(path, parent))).ToArray();
        if (_request.Action is ProjectFileAction.CreateFile or ProjectFileAction.CreateDirectory)
        {
            var target = ProjectFilePaths.Editable(_request.Target); EnsureFree(target);
            if (_request.Action == ProjectFileAction.CreateDirectory) _afterDirectories.Add(target);
            else { _after[target] = Store(_request.Content ?? []); AddMetadata(target, null, true); }
        }
        else foreach (var source in sources)
        {
            var full = _request.Action == ProjectFileAction.Import ? source : _reader.Resolve(source);
            if (!File.Exists(full) && !Directory.Exists(full)) throw ProjectFilePaths.Error("missing", source);
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw ProjectFilePaths.Error("path", source);
            if (_request.Action == ProjectFileAction.Metadata && Directory.Exists(full)) throw ProjectFilePaths.Error("category", source);
            string? target = _request.Action is ProjectFileAction.Delete or ProjectFileAction.Metadata ? null
                : ProjectFilePaths.Editable(_request.TargetIsDirectory ? (_request.Target.Length == 0 ? "" : _request.Target.TrimEnd('/') + "/") + Path.GetFileName(source) : _request.Target);
            if (target is not null)
            {
                if (_request.UniqueNames && _request.Action is ProjectFileAction.Copy or ProjectFileAction.Import)
                {
                    var original = target; var suffix = 1;
                    while (_targets.Contains(target) || File.Exists(_reader.Resolve(target)) || Directory.Exists(_reader.Resolve(target)))
                    {
                        var extension = File.Exists(full) ? Path.GetExtension(original) : "";
                        target = original[..(original.Length - extension.Length)] + " " + suffix++ + extension;
                    }
                }
                var parent = Path.GetDirectoryName(target)?.Replace(Path.DirectorySeparatorChar, '/') ?? "";
                if (!Directory.Exists(_reader.Resolve(parent))) throw ProjectFilePaths.Error("missing", parent);
                if (_request.Action != ProjectFileAction.Import && ProjectFilePaths.Category(source) != ProjectFilePaths.Category(target)) throw ProjectFilePaths.Error("category", target);
                if (_request.Action != ProjectFileAction.Import && ProjectFilePaths.Within(target, source) && !ProjectFilePaths.Comparer.Equals(source, target)) throw ProjectFilePaths.Error("path", target);
                var caseRename = OperatingSystem.IsWindows() && source != target && ProjectFilePaths.Comparer.Equals(source, target);
                if (!_targets.Add(target)) throw ProjectFilePaths.Error("collision", target);
                if (!caseRename) EnsureFree(target);
                if (caseRename && (_request.Action != ProjectFileAction.Move || sources.Length != 1)) throw ProjectFilePaths.Error("path", source);
                _caseOnly = caseRename;
                if (_request.Action == ProjectFileAction.Move) _moves.Add((source, target));
                if (_request.Action == ProjectFileAction.Copy) _variantCopies.Add((source, target));
            }
            var tree = Enumerate(full);
            if (target is not null && tree.Directories.Count > 0) _roots.Add(target);
            var consume = _request.Action is ProjectFileAction.Move or ProjectFileAction.Delete or ProjectFileAction.Metadata;
            if (consume) _roots.Add(source);
            foreach (var directory in tree.Directories)
            {
                var relative = _reader.Relative(directory);
                if (consume) _beforeDirectories.Add(relative);
                if (target is not null) _afterDirectories.Add(Relocate(directory, full, target));
            }
            foreach (var file in tree.Files)
            {
                var relative = _reader.Relative(file);
                if (consume) _before[relative] = SnapshotFile(file);
                if (_request.Action == ProjectFileAction.Metadata) { _after[relative] = _before[relative]; continue; }
                if (target is not null) _after[Relocate(file, full, target)] = SnapshotFile(file);
            }
            if (File.Exists(full) && source.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) && File.Exists(full + ".meta"))
            {
                if (consume) _before[source + ".meta"] = SnapshotFile(full + ".meta");
                if (_caseOnly) _moves.Add((source + ".meta", target! + ".meta"));
            }
            foreach (var file in tree.Files.Where(file => !file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
            {
                var destination = target is null ? source : Relocate(file, full, target);
                if (_request.Action != ProjectFileAction.Delete) AddMetadata(destination, file, _request.Action is ProjectFileAction.Copy or ProjectFileAction.Import or ProjectFileAction.CreateFile);
            }
        }
        if (sources.Length == 0 && _request.Action is not (ProjectFileAction.CreateFile or ProjectFileAction.CreateDirectory)) throw ProjectFilePaths.Error("missing", "selection");
        foreach (var node in working.Graph.Nodes.Where(node => node.Type == "Group" && _roots.Any(root => ProjectFilePaths.Within("Graph/" + node.File, root))))
        {
            if (_request.Action == ProjectFileAction.Move)
            {
                var path = Map("Graph/" + node.File, true);
                if (!path.EndsWith(".rawgalgroup", StringComparison.OrdinalIgnoreCase)) throw ProjectFilePaths.Error("category", path);
                _managed.Add(path);
            }
        }
        if (_request.Action == ProjectFileAction.Delete)
        {
            References = FindReferences(working);
        }
        // A copied sidecar is owned by its asset; reject collisions before publication.
        foreach (var path in _after.Keys.Where(path => !_before.ContainsKey(path) && !_caseOnly)) EnsureFree(path);
        foreach (var path in _after.Keys.Concat(_afterDirectories.ToArray()).ToArray())
        {
            var parent = Path.GetDirectoryName(path)?.Replace(Path.DirectorySeparatorChar, '/');
            while (!string.IsNullOrEmpty(parent) && !Directory.Exists(_reader.Resolve(parent))) { _afterDirectories.Add(parent); parent = Path.GetDirectoryName(parent)?.Replace(Path.DirectorySeparatorChar, '/'); }
        }
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in _after.Where(pair => pair.Key.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) && pair.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
            if (!ids.Add(_codec.Deserialize(File.ReadAllText(pair.Value.Backup), pair.Key[7..^5]).Id)) throw ProjectFilePaths.Error("guidCollision", pair.Key);
        if (ids.Count > 0 && Directory.Exists(_reader.Resolve("Assets")))
            foreach (var path in Enumerate(_reader.Resolve("Assets")).Files.Where(path => path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
            {
                var relative = _reader.Relative(path);
                if (_before.Keys.Contains(relative, ProjectFilePaths.Comparer) || _after.Keys.Contains(relative, ProjectFilePaths.Comparer)) continue;
                var id = _codec.Deserialize(File.ReadAllText(path), relative[7..^5]).Id;
                if (ids.Contains(id)) throw ProjectFilePaths.Error("guidCollision", relative);
            }
        PrepareVariantRelations();
        _initialized = true;
    }

    private IReadOnlyList<ProjectFileReference> FindReferences(EditorProjectDocument document)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _before.Keys.Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
        {
            values.Add(path); if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) values.Add(path[7..]);
            if (_before.TryGetValue(path + ".meta", out var meta)) values.Add(_codec.Deserialize(File.ReadAllText(meta.Backup), path[7..]).Id);
        }
        var references = new List<ProjectFileReference>();
        foreach (var pair in document.GroupEntries)
        {
            if (_request.DeleteGroups && document.Graph.Nodes.Any(node => node.Id == pair.Key && _roots.Any(root => ProjectFilePaths.Within("Graph/" + node.File, root)))) continue;
            foreach (var entry in pair.Value)
                foreach (var parameter in entry.Parameters)
                {
                    var schema = _catalog.Definitions.FirstOrDefault(item => item.Type == entry.Type);
                    var editor = schema?.DynamicParameters?.GetValueOrDefault(parameter.Key)?.Editor?.EditorId;
                    foreach (var text in Strings(parameter.Value))
                        if (values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase))) references.Add(new("group/" + pair.Key + "/entry/" + entry.StableId, parameter.Key, values.Contains(text) && editor?.Contains("asset", StringComparison.OrdinalIgnoreCase) == true));
                }
        }
        if (Strings(JsonSerializer.SerializeToElement(document.Configuration)).Any(text => values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase))))
            references.Add(new("settings.json", "custom", false));
        foreach (var file in document.Files.Values.Where(file => file.Exists && file.ManagedContent && !_roots.Any(root => ProjectFilePaths.Within(file.Path, root))))
        {
            using var stream = new MemoryStream(file.Data, writable: false);
            using var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            var text = reader.ReadToEnd();
            if (values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase))) references.Add(new(file.Path, "custom", false));
        }
        foreach (var file in new[] { "UI/ui.json" }.Where(path => File.Exists(_reader.Resolve(path)) && !document.Files.Values.Any(file => file.Exists && file.ManagedContent && ProjectFilePaths.Comparer.Equals(file.Path, path))))
        {
            var text = File.ReadAllText(_reader.Resolve(file));
            if (values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase))) references.Add(new(file, "custom", false));
        }
        return references.Distinct().ToArray();
    }
    private static IEnumerable<string> Strings(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => [value.GetString()!],
        JsonValueKind.Array => value.EnumerateArray().SelectMany(Strings),
        JsonValueKind.Object => value.EnumerateObject().SelectMany(item => Strings(item.Value)),
        _ => []
    };
    private void AddMetadata(string destination, string? original, bool newId)
    {
        if (!destination.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || destination.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return;
        AssetMeta meta;
        if (original is not null && File.Exists(original + ".meta"))
        {
            var json = File.ReadAllText(original + ".meta");
            meta = _codec.Deserialize(json, destination[7..]);
            // Readers may tolerate malformed optional fields; an authoring operation must not discard them.
            try { _ = JsonSerializer.Deserialize(json, _types.Get(meta.TypeId).MetaDtoType, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
            catch (JsonException error) { throw new InvalidDataException($"Invalid asset metadata: '{original}.meta'.", error); }
        }
        else
        {
            var type = _types.TryGetByExtension(Path.GetExtension(destination), out var registration) ? registration : _types.Get(_types.DefaultTypeId ?? throw new InvalidDataException("No default resource type was selected."));
            meta = (AssetMeta)Activator.CreateInstance(type.MetaDtoType)!; meta.TypeId = type.TypeId; meta.Id = Guid.NewGuid().ToString("N");
        }
        if (newId) meta.Id = Guid.NewGuid().ToString("N");
        var annotations = meta.ReadGallery();
        if (newId && annotations.Count > 0)
        {
            if (_galleryIds is null)
            {
                _galleryIds = [];
                if (Directory.Exists(_reader.Resolve("Assets")))
                    foreach (var path in Enumerate(_reader.Resolve("Assets")).Files.Where(path => path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
                        foreach (var item in _codec.Deserialize(File.ReadAllText(path), _reader.Relative(path)[7..^5]).ReadGallery()) _galleryIds.Add(item.Id);
            }
            meta.SetGallery(annotations.Select(item =>
            {
                if (item.Id <= 0) throw ProjectFilePaths.Error("galleryId", destination);
                if (_galleryIds.Add(item.Id)) return item;
                var id = 1; while (_galleryIds.Contains(id)) id = checked(id + 1);
                _galleryIds.Add(id); return item with { Id = id };
            }));
        }
        meta.Path = destination[7..];
        if (_request.Action == ProjectFileAction.Metadata) { if (meta is SpriteAssetMeta sprite && _request.Filter is not null) sprite.Filter = _request.Filter; if (_request.Compression is not null) meta.Compress = _request.Compression; }
        _after[destination + ".meta"] = Store(System.Text.Encoding.UTF8.GetBytes(_codec.Serialize(meta)));
        if (_request.Action == ProjectFileAction.Metadata && original is not null && File.Exists(original + ".meta")) _before[destination + ".meta"] = SnapshotFile(original + ".meta");
    }
    private static string Relocate(string file, string source, string target) => file == source ? target : target + "/" + Path.GetRelativePath(source, file).Replace('\\', '/');
    private void EnsureFree(string path) { if (File.Exists(_reader.Resolve(path)) || Directory.Exists(_reader.Resolve(path))) throw ProjectFilePaths.Error("collision", path); }
    private string Map(string path, bool forward)
    {
        foreach (var (from, to) in _moves)
        {
            var source = forward ? from : to; var target = forward ? to : from;
            if (ProjectFilePaths.Within(path, source)) return target + path[source.Length..];
        }
        return path;
    }
    private Snapshot SnapshotFile(string file)
    {
        var destination = Path.Combine(_retained, Guid.NewGuid().ToString("N")); File.Copy(file, destination);
        var length = new FileInfo(destination).Length; _storedBytes += length;
        return new(destination, AuthorFileTransaction.ReadHash(destination)!, length);
    }
    private Snapshot Store(byte[] content) { var path = Path.Combine(_retained, Guid.NewGuid().ToString("N")); File.WriteAllBytes(path, content); _storedBytes += content.LongLength; return new(path, AuthorFileTransaction.ReadHash(path)!, content.LongLength); }
    private sealed record Snapshot(string Backup, string Hash, long Length);
    private static (List<string> Files, List<string> Directories) Enumerate(string full)
    {
        if (File.Exists(full)) return ([full], []);
        var files = new List<string>(); var directories = new List<string> { full };
        for (var i = 0; i < directories.Count; i++) foreach (var path in Directory.EnumerateFileSystemEntries(directories[i]))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw ProjectFilePaths.Error("path", path);
            if (Directory.Exists(path)) directories.Add(path); else files.Add(path);
        }
        return (files, directories);
    }
    private RemovedContent CaptureRemoved(EditorProjectDocument document)
    {
        var nodes = document.Graph.Nodes.Where(node => node.Type == "Group" && _roots.Any(root => ProjectFilePaths.Within("Graph/" + node.File, root))).ToArray();
        var ids = nodes.Select(node => node.Id).ToHashSet();
        return new(Clone(new EditorProjectDocument { Graph = new() { Nodes = nodes.ToList(), Edges = document.Graph.Edges.Where(edge => ids.Contains(edge.FromNodeId) || ids.Contains(edge.ToNodeId)).ToList() }, GroupEntries = document.GroupEntries.Where(pair => ids.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value), Files = document.Files.Where(pair => _roots.Any(root => ProjectFilePaths.Within(pair.Value.Path, root))).ToDictionary(pair => pair.Key, pair => pair.Value) }),
            document.Graph.Nodes.Select((node, index) => (node.Id, index)).ToDictionary(item => item.Id, item => item.index),
            document.Graph.Edges.Select((edge, index) => (edge.Id, index)).ToDictionary(item => item.Id, item => item.index));
    }
    private sealed record RemovedContent(EditorProjectDocument Document, Dictionary<string, int> Nodes, Dictionary<string, int> Edges);
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;
    private sealed record FileChange(ProjectFileOperation Owner, bool Forward, ProjectFilePlan Files, RemovedContent? Working, RemovedContent? Saved) : IProjectFileStructureChange
    {
        public void Apply(EditorProjectDocument document, AuthorDocumentVersion version)
        {
            var removed = version == AuthorDocumentVersion.Working ? Working : Saved;
            foreach (var file in document.Files.Values)
            {
                if (Owner.Action == ProjectFileAction.Move && file.Exists && file.ManagedContent) Owner.RebaseVariantBuffer(file, Forward);
                if (Owner.Action == ProjectFileAction.Move) file.Path = Owner.Map(file.Path, Forward);
                if (Owner.Action == ProjectFileAction.Delete && Owner._roots.Any(root => ProjectFilePaths.Within(file.Path, root)))
                {
                    file.Exists = !Forward;
                    if (!Forward && removed?.Document.Files.TryGetValue(file.Id, out var previous) == true) file.Data = previous.Data.ToArray();
                }
            }
            if (Owner.Action == ProjectFileAction.Move)
                foreach (var node in document.Graph.Nodes.Where(node => node.Type == "Group")) node.File = Owner.Map("Graph/" + node.File, Forward)[6..];
            if (Owner.Action != ProjectFileAction.Delete) return;
            if (removed is null) return;
            var ids = removed.Document.Graph.Nodes.Select(node => node.Id).ToHashSet();
            if (Forward)
            {
                document.Graph.Nodes.RemoveAll(node => ids.Contains(node.Id)); document.Graph.Edges.RemoveAll(edge => ids.Contains(edge.FromNodeId) || ids.Contains(edge.ToNodeId));
                foreach (var id in ids) document.GroupEntries.Remove(id);
            }
            else
            {
                if (document.Graph.Nodes.Any(node => ids.Contains(node.Id))) throw ProjectFilePaths.Error("changed", "Graph");
                var copy = Clone(removed.Document);
                foreach (var node in copy.Graph.Nodes.OrderBy(node => removed.Nodes[node.Id])) document.Graph.Nodes.Insert(Math.Min(removed.Nodes[node.Id], document.Graph.Nodes.Count), node);
                foreach (var edge in copy.Graph.Edges.OrderBy(edge => removed.Edges[edge.Id])) document.Graph.Edges.Insert(Math.Min(removed.Edges[edge.Id], document.Graph.Edges.Count), edge);
                foreach (var pair in copy.GroupEntries) document.GroupEntries.Add(pair.Key, pair.Value);
            }
        }
    }
    public static void CleanupOrphans(string projectPath)
    {
        var reader = new ProjectRawReader(projectPath);
        var root = reader.Resolve(".galnet/editor/history");
        if (!Directory.Exists(root)) return;
        foreach (var candidate in Directory.EnumerateDirectories(root))
        {
            var directory = reader.Resolve(reader.Relative(candidate));
            var owner = reader.Resolve(reader.Relative(Path.Combine(directory, "owner.lock")));
            if (!File.Exists(owner) && Directory.GetCreationTimeUtc(directory) > DateTime.UtcNow.AddMinutes(-1)) continue;
            try
            {
                using (new FileStream(owner, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
                Directory.Delete(directory, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        var path = _reader.Resolve(".galnet/editor/history/" + Id);
        if (path != _retained) throw new InvalidOperationException("Invalid retained-file root.");
        _lease?.Dispose();
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}
