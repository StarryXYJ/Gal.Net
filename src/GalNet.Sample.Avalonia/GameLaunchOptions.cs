namespace GalNet.Sample.Avalonia;

internal sealed record GameLaunchOptions(string? GameDirectory, string? ProfileDirectory)
{
    public static GameLaunchOptions Parse(string[] args)
    {
        string? gameDirectory = null;
        string? profileDirectory = null;

        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--profile" && index + 1 < args.Length)
            {
                profileDirectory = Path.GetFullPath(args[++index]);
                continue;
            }

            if (!args[index].StartsWith("-", StringComparison.Ordinal) && gameDirectory is null)
                gameDirectory = Path.GetFullPath(args[index]);
        }

        return new GameLaunchOptions(gameDirectory, profileDirectory);
    }
}
