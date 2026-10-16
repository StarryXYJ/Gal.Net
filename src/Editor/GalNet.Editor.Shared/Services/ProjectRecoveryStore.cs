using System.Security.Cryptography;
using System.Text.Json;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.Shared.Services;

/// <summary>Independent author snapshots; these are recovery material, never a persisted undo stack.</summary>
public sealed class ProjectRecoveryStore(string projectPath, ProjectSessionOptions options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private string? _lastHash;

    public string? Capture(EditorProjectDocument document)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(payload));
        if (_lastHash == hash) return null;
        var reader = new ProjectRawReader(projectPath);
        var directory = reader.Resolve(".galnet/editor/recovery");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".json");
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new RecoverySnapshot(1, DateTime.UtcNow, hash, payload), JsonOptions));
            File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        _lastHash = hash;
        var snapshots = new DirectoryInfo(directory).EnumerateFiles("*.json").OrderByDescending(file => file.Name, StringComparer.Ordinal).ToArray();
        long retained = 0;
        for (var index = 0; index < snapshots.Length; index++)
        {
            var file = snapshots[index];
            retained += file.Length;
            // Keep the newest complete snapshot even when it alone exceeds the soft budget.
            if (index > 0 && (index >= Math.Max(1, options.RecoveryCopies) || retained > options.RetainedByteBudget)) file.Delete();
        }
        return path;
    }

    public static EditorProjectDocument Read(string path)
    {
        var snapshot = JsonSerializer.Deserialize<RecoverySnapshot>(File.ReadAllBytes(path), JsonOptions)
            ?? throw new InvalidDataException("Recovery snapshot is empty.");
        if (snapshot.Version != 1 || Convert.ToHexString(SHA256.HashData(snapshot.Payload)) != snapshot.Hash)
            throw new InvalidDataException("Recovery snapshot is corrupt or has an unsupported version.");
        return JsonSerializer.Deserialize<EditorProjectDocument>(snapshot.Payload, JsonOptions)
            ?? throw new InvalidDataException("Recovery snapshot has no author document.");
    }

    private sealed record RecoverySnapshot(int Version, DateTime CreatedUtc, string Hash, byte[] Payload);
}
