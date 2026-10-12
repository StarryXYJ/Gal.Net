using System.Text.Json;
using GalNet.Runtime.Progress;

namespace GalNet.Storage.FileSystem;

/// <summary>File-backed, per-player read progress.</summary>
public sealed class FileGameProgressService : IGameProgressService
{
    private readonly string _path;
    private readonly object _sync = new();
    private ProgressData _data;

    public FileGameProgressService(string profileDirectory)
    {
        _path = Path.Combine(profileDirectory, "progress.json");
        _data = File.Exists(_path)
            ? JsonSerializer.Deserialize<ProgressData>(File.ReadAllText(_path))
                ?? throw new InvalidDataException("Read progress document is empty.")
            : new() { Version = 2 };
        if (_data.Version != 2)
            throw new InvalidDataException($"Unsupported read progress version '{_data.Version}'.");
    }

    public bool IsRead(string groupId, string entryId) { lock (_sync) return _data.ReadEntries.Contains(Key(groupId, entryId)); }
    public void MarkRead(string groupId, string entryId)
    {
        lock (_sync)
        {
            var key = Key(groupId, entryId);
            if (!_data.ReadEntries.Add(key)) return;
            try { Save(); }
            catch { _data.ReadEntries.Remove(key); throw; }
        }
    }

    /// <summary>Clears all persisted read progress for a fresh player profile.</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _data = new ProgressData { Version = 2 };
            if (File.Exists(_path))
                File.Delete(_path);
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_data));
        File.Move(temporaryPath, _path, overwrite: true);
    }

    private static string Key(string groupId, string entryId) => JsonSerializer.Serialize(new[] { groupId, entryId });

    private sealed class ProgressData
    {
        public int Version { get; set; }
        public HashSet<string> ReadEntries { get; set; } = [];
    }
}
