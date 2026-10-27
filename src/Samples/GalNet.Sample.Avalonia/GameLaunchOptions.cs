#if GALNET_GAME_DEBUG
using GalNet.Debugging;
#endif

namespace GalNet.Sample.Avalonia;

internal sealed record GameLaunchOptions(string? GameDirectory, string? ProfileDirectory)
{
#if GALNET_GAME_DEBUG
    public DebugInvitation? DebugInvitation { get; init; }
#endif
    public string LogDirectory =>
        Path.Combine(
            ProfileDirectory
            ?? (GameDirectory is null
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GalNet", "Sample")
                : Path.Combine(GameDirectory, ".galnet")),
            "logs");

    public static GameLaunchOptions Parse(string[] args)
    {
        string? gameDirectory = null;
        string? profileDirectory = null;
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
            if (args[index] == "--profile" && index + 1 < args.Length)
            {
                profileDirectory = Path.GetFullPath(args[++index]);
                continue;
            }

            if (!args[index].StartsWith('-') && gameDirectory is null)
                gameDirectory = Path.GetFullPath(args[index]);
        }

#if GALNET_GAME_DEBUG
        DebugInvitation? invitation = null;
        if (debug.Count > 0)
        {
            if (gameDirectory is null || !debug.TryGetValue("--debug-port", out var portText) || !int.TryParse(portText, out var port) || port is <= 0 or > 65535 ||
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
