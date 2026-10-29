using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GalNet.Assets;
using GalNet.Storage.FileSystem;
#if GALNET_GAME_DEBUG
using GalNet.Debugging;
#endif

namespace GalNet.Sample.Avalonia;

internal sealed record GameLaunchOptions(string? GameDirectory, string? ProfileDirectory)
{
#if GALNET_GAME_DEBUG
    public DebugInvitation? DebugInvitation { get; init; }
#endif
    public string LogDirectory => Path.Combine(ProfileDirectory
        ?? GetDefaultProfileDirectory(GameDirectory ?? ApplicationPackageLayout.GetContentDirectory(GetApplicationDirectory(AppContext.BaseDirectory))), "logs");

    public static GameLaunchOptions Parse(string[] args)
        => Parse(args, GetApplicationDirectory(AppContext.BaseDirectory));

    internal static string GetApplicationDirectory(string assemblyDirectory)
        => ApplicationPackageLayout.FindApplicationRoot(assemblyDirectory);

    internal static string GetDefaultProfileDirectory(string gameDirectory)
    {
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
            // Content loading reports the original error in the startup window.
        }
        var projectKey = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GalNet", projectKey);
    }

    internal static GameLaunchOptions Parse(string[] args, string applicationDirectory)
    {
        var gameDirectory = ApplicationPackageLayout.GetContentDirectory(Path.GetFullPath(applicationDirectory));
        string? profileDirectory = null;
#if GALNET_LAUNCH_ARGS
        var hasGameArgument = false;
#if GALNET_GAME_DEBUG
        var debug = new Dictionary<string, string>(StringComparer.Ordinal);
#endif

        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].StartsWith("--debug-", StringComparison.Ordinal))
            {
#if GALNET_GAME_DEBUG
                var key = args[index];
                if (index + 1 >= args.Length) throw new ArgumentException($"Missing value for '{key}'.");
                debug.Add(key, args[++index]);
                continue;
#else
                throw new ArgumentException("This application was built without game debugging support.");
#endif
            }
            if (args[index] == "--profile")
            {
                if (index + 1 >= args.Length || args[index + 1].StartsWith('-'))
                    throw new ArgumentException("Missing value for '--profile'.");
                profileDirectory = Path.GetFullPath(args[++index]);
                continue;
            }

            if (!args[index].StartsWith('-') && !hasGameArgument)
            {
                gameDirectory = Path.GetFullPath(args[index]);
                hasGameArgument = true;
                continue;
            }
            throw new ArgumentException($"Unknown launch argument '{args[index]}'.");
        }
#else
        if (args.Length > 0) throw new ArgumentException("This player was built without launch argument support.");
#endif
        profileDirectory ??= GetDefaultProfileDirectory(gameDirectory);

#if GALNET_GAME_DEBUG
        DebugInvitation? invitation = null;
        if (debug.Count > 0)
        {
            if (!debug.TryGetValue("--debug-port", out var portText) || !int.TryParse(portText, out var port) || port is <= 0 or > 65535 ||
                !debug.TryGetValue("--debug-token", out var token) || !debug.TryGetValue("--debug-session", out var session) || !debug.TryGetValue("--debug-project", out var project))
                throw new ArgumentException("Debug launch requires a content directory, port, token, session and project identity.");
            string? fingerprint = null;
            if (debug.TryGetValue("--debug-contract", out var expected))
            {
                fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(GalNet.Project.Contracts.ProjectSchemas).Assembly.Location)));
                if (!string.Equals(expected, fingerprint, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"The App contract differs from the Editor project. Expected SHA256: {expected}; actual SHA256: {fingerprint}. Deploy the App's matching Contracts and reopen the Editor project before debugging.");
            }
            invitation = new(port, token, session, new(project, DebugContentIdentity.Compute(gameDirectory), fingerprint));
        }
        return new GameLaunchOptions(gameDirectory, profileDirectory) { DebugInvitation = invitation };
#else
        return new GameLaunchOptions(gameDirectory, profileDirectory);
#endif
    }
}
