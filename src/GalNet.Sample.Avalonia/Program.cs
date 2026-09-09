using Avalonia;
using GalNet.Runtime.Logging;
using Serilog;

namespace GalNet.Sample.Avalonia;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var launchOptions = GameLaunchOptions.Parse(args);
        Directory.CreateDirectory(launchOptions.LogDirectory);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.WithProperty("LogChannel", "Game")
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3} {LogChannel}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                Path.Combine(launchOptions.LogDirectory, "game-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3} {LogChannel}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            App.LaunchOptions = launchOptions;
            GameLog.Logger.Information(
                "Avalonia sample starting. GameDirectory={GameDirectory}, ProfileDirectory={ProfileDirectory}, LogDirectory={LogDirectory}",
                launchOptions.GameDirectory, launchOptions.ProfileDirectory, launchOptions.LogDirectory);
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            GameLog.Logger.Fatal(exception, "Avalonia sample terminated unexpectedly");
        }
        finally
        {
            GameLog.Logger.Information("Avalonia sample stopped");
            Log.CloseAndFlush();
        }
    }

    // Initialization code. Do not use Avalonia before AppMain is called.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
