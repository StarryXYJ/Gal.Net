using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GalNet.Assets;

namespace GalNet.Storage.FileSystem;

/// <summary>Stable player directory identity; the application supplies its writable root.</summary>
public static class GameProfilePaths
{
    public static string GetProfileDirectory(string gameDirectory, string profileRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileRoot);
        if (!Path.IsPathFullyQualified(profileRoot)) throw new ArgumentException("The profile root must be absolute.", nameof(profileRoot));
        var identity = "directory:" + Path.GetFullPath(gameDirectory);
        try
        {
            if (File.Exists(Path.Combine(gameDirectory, PackedGameDocuments.GameFileName)))
                identity = "project:" + InstalledGameDescriptor.Read(gameDirectory).ProjectId;
            else
            {
                var descriptor = Path.Combine(gameDirectory, "content.json");
                if (File.Exists(descriptor))
                {
                    using var json = JsonDocument.Parse(File.ReadAllBytes(descriptor));
                    if (json.RootElement.ValueKind == JsonValueKind.Object
                        && json.RootElement.TryGetProperty("projectId", out var id)
                        && id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()))
                        identity = "project:" + id.GetString();
                }
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            // Formal content loading reports the original failure in the startup view.
        }
        var projectKey = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return Path.Combine(profileRoot, projectKey);
    }
}
