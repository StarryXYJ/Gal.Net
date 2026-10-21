using GalNet.Debugging;

namespace GalNet.Sample.Avalonia;

internal sealed record GameLaunchOptions(string? GameDirectory, string? ProfileDirectory)
{
    public DebugInvitation? DebugInvitation { get; init; }
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
        var debug = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].StartsWith("--debug-", StringComparison.Ordinal))
            {
                var key = args[index];
                if (index + 1 >= args.Length) throw new ArgumentException($"Missing value for '{key}'.");
                debug.Add(key, args[++index]);
                continue;
            }
            if (args[index] == "--profile" && index + 1 < args.Length)
            {
                profileDirectory = Path.GetFullPath(args[++index]);
                continue;
            }

            if (!args[index].StartsWith('-') && gameDirectory is null)
                gameDirectory = Path.GetFullPath(args[index]);
        }

        DebugInvitation? invitation = null;
        if (debug.Count > 0)
        {
            if (gameDirectory is null || !debug.TryGetValue("--debug-port", out var portText) || !int.TryParse(portText, out var port) || port is <= 0 or > 65535 ||
                !debug.TryGetValue("--debug-token", out var token) || !debug.TryGetValue("--debug-session", out var session) || !debug.TryGetValue("--debug-project", out var project))
                throw new ArgumentException("Debug launch requires a content directory, port, token, session and project identity.");
            invitation = new(port, token, session, new(project, DebugContentIdentity.Compute(gameDirectory)));
        }
        return new GameLaunchOptions(gameDirectory, profileDirectory) { DebugInvitation = invitation };
    }
}
