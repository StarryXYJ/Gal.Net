using Serilog;

namespace GalNet.Runtime.Logging;

public static class GameLog
{
    public static ILogger Logger => Log.ForContext("LogChannel", "Game");
}
