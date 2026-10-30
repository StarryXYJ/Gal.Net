using System.Security.Cryptography;
using GalNet.Assets;
using GalNet.Storage.FileSystem;
#if GALNET_GAME_DEBUG
using GalNet.Debugging;
using GalNet.Avalonia.Debugging;
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

    internal static string GetDefaultProfileDirectory(string gameDirectory) =>
        SampleCustomization.GetProfileDirectory(gameDirectory);

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
        var invitation = DebugLaunchArguments.Parse(debug, gameDirectory, typeof(GalNet.Project.Contracts.ProjectSchemas).Assembly.Location);
        return new GameLaunchOptions(gameDirectory, profileDirectory) { DebugInvitation = invitation };
#else
        return new GameLaunchOptions(gameDirectory, profileDirectory);
#endif
    }
}
