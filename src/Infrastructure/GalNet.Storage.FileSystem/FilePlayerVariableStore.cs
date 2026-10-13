using System.Text.Json;
using GalNet.Runtime.Persistence;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GalNet.Storage.FileSystem;

/// <summary>File-backed store for player variables that survive individual save slots.</summary>
public sealed class FilePlayerVariableStore : IPlayerVariableStore, IDisposable
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FilePlayerVariableStore(string profileDirectory) =>
        _path = Path.Combine(profileDirectory, "player-variables.json");

    public async Task<IReadOnlyDictionary<string, GalVariable>> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(_path)) return new Dictionary<string, GalVariable>(StringComparer.Ordinal);
            var json = await File.ReadAllTextAsync(_path, ct);
            var stored = JsonSerializer.Deserialize<Dictionary<string, GalVariable>>(json) ?? [];
            return stored.ToDictionary(pair => pair.Key, pair => Clone(pair.Key, pair.Value), StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, GalVariable>(StringComparer.Ordinal);
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(IReadOnlyDictionary<string, GalVariable> variables, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var copy = variables.ToDictionary(pair => pair.Key, pair => Clone(pair.Key, pair.Value), StringComparer.Ordinal);
            var temporaryPath = _path + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(copy), ct);
            File.Move(temporaryPath, _path, true);
        }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private static GalVariable Clone(string name, GalVariable source) => source.Clone(name);
}
