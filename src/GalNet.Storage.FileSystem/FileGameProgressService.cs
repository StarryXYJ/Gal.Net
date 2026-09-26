using System.Text.Json;
using GalNet.Core.Services;

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
        try { _data = File.Exists(_path) ? JsonSerializer.Deserialize<ProgressData>(File.ReadAllText(_path)) ?? new() : new(); }
        catch { _data = new(); }
    }

    public bool IsRead(string groupId, string entryId) { lock (_sync) return _data.ReadEntries.Contains(Key(groupId, entryId)); }
    public void MarkRead(string groupId, string entryId) { lock (_sync) { if (_data.ReadEntries.Add(Key(groupId, entryId))) Save(); } }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_data));
    }

    private static string Key(string groupId, string entryId) => $"{groupId}/{entryId}";

    private sealed class ProgressData
    {
        public HashSet<string> ReadEntries { get; set; } = [];
    }
}
