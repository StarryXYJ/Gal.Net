namespace GalNet.Sample.Headless;

internal sealed record HeadlessOptions(string GameDirectory, string ProfileDirectory, int? SaveSlot, int? LoadSlot)
{
    public static bool TryParse(string[] args, out HeadlessOptions? options, out string? error)
    {
        options = null;
        error = null;
        if (args.Length == 0)
        {
            error = "A game data directory is required.";
            return false;
        }

        var gameDirectory = Path.GetFullPath(args[0]);
        var profileDirectory = Path.Combine(gameDirectory, ".galnet");
        int? saveSlot = null;
        int? loadSlot = null;
        for (var index = 1; index < args.Length; index++)
        {
            var option = args[index];
            if (option is not "--profile" and not "--save-slot" and not "--load-slot")
            {
                error = $"Unknown option: {option}";
                return false;
            }
            if (++index >= args.Length)
            {
                error = $"Option {option} requires a value.";
                return false;
            }

            switch (option)
            {
                case "--profile": profileDirectory = Path.GetFullPath(args[index]); break;
                case "--save-slot" when int.TryParse(args[index], out var save) && save >= 0: saveSlot = save; break;
                case "--load-slot" when int.TryParse(args[index], out var load) && load >= 0: loadSlot = load; break;
                default:
                    error = $"Option {option} requires a non-negative slot number.";
                    return false;
            }
        }

        options = new HeadlessOptions(gameDirectory, profileDirectory, saveSlot, loadSlot);
        return true;
    }
}
