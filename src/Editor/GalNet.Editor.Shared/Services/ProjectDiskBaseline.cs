using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Commands;

namespace GalNet.Editor.Shared.Services;

/// <summary>Accepted disk bytes for external-change detection, independent of the author dirty baseline.
/// The content session owns publication ordering and synchronization.</summary>
internal sealed class ProjectDiskBaseline
{
    private readonly ProjectRawReader _reader;
    private Dictionary<string, byte[]> _files;
    private byte[] _settings;

    public ProjectDiskBaseline(string root, IReadOnlyDictionary<string, byte[]?> authorSources)
    {
        _reader = new(root);
        _files = ExternalProjectSnapshotReader.ReadFiles(root, []);
        foreach (var (path, bytes) in authorSources)
            if (bytes is not null) Track(path, bytes);
        ContractsFingerprint = ExternalProjectSnapshotReader.ContractsFingerprint(root);
        _settings = File.ReadAllBytes(_reader.Resolve("settings.json"));
    }

    public string ContractsFingerprint { get; }
    public IEnumerable<KeyValuePair<string, byte[]>> LocalizationFiles =>
        _files.Where(pair => ExternalProjectSnapshotReader.IsLocalization(pair.Key));

    public void Track(string path, byte[] bytes) => _files[path] = bytes.ToArray();

    public bool Matches(ExternalProjectSnapshot snapshot) => ExternalProjectSnapshotReader.Same(snapshot.Files, _files);

    public void CheckSettings(string message)
    {
        if (!File.ReadAllBytes(_reader.Resolve("settings.json")).AsSpan().SequenceEqual(_settings))
            throw new EditorOperationException("project.externalSettings", message);
    }

    public void AcceptReload(ExternalProjectSnapshot snapshot)
    {
        AcceptSettings(snapshot.Files["settings.json"]);
        _files = snapshot.Files.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), ProjectFilePaths.Comparer);
    }

    // Compensation advances the settings check without accepting the failed publication's observed files.
    public void AcceptSettings(byte[] settings) => _settings = settings.ToArray();

    public void AcceptPublication(ProjectSavePlan plan, IEnumerable<EditorFileDocument> tracked)
    {
        AcceptSettings(plan.Writes["settings.json"]);
        foreach (var (path, bytes) in plan.Writes)
            if (path == "settings.json" || path.StartsWith("Graph/", StringComparison.Ordinal) || _files.ContainsKey(path) || ExternalProjectSnapshotReader.IsLocalization(path))
                Track(path, bytes);
        foreach (var path in plan.DeletedFiles) _files.Remove(path);
        if (plan.Files is not { } files) return;
        foreach (var path in files.Writes.Keys.Concat(files.Copies.Keys).Where(path =>
            path.StartsWith("Graph/", StringComparison.Ordinal) || path.StartsWith("I18n/", StringComparison.Ordinal) || tracked.Any(file => file.ManagedContent && file.Path == path)))
            _files[path] = File.ReadAllBytes(_reader.Resolve(path));
        foreach (var path in files.Deletes) _files.Remove(path);
    }
}
