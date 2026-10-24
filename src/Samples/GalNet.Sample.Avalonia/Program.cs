using System.Globalization;
using Avalonia;
using GalNet.Avalonia.Themes;
using GalNet.Runtime.Logging;
using GalNet.Sample.Avalonia.Debug;
using Serilog;

namespace GalNet.Sample.Avalonia;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
#if BENCHMARK
        GalNet.Diagnostics.StartupBenchmark.Start();
#endif
        try
        {
            var launchOptions = GameLaunchOptions.Parse(args);
            Directory.CreateDirectory(launchOptions.LogDirectory);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.WithProperty("LogChannel", "Game")
                .WriteTo.Console(
                    formatProvider: CultureInfo.InvariantCulture,
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3} {LogChannel}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    Path.Combine(launchOptions.LogDirectory, "game-.log"),
                    formatProvider: CultureInfo.InvariantCulture,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    shared: true,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3} {LogChannel}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.Sink(SampleDebugLogStore.Shared)
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
                throw;
            }
            finally
            {
                GameLog.Logger.Information("Avalonia sample stopped");
                Log.CloseAndFlush();
#if BENCHMARK
                if (GalNet.Diagnostics.StartupBenchmark.Failed) Environment.ExitCode = 1;
#endif
            }
        }
        catch (Exception exception)
        {
#if BENCHMARK
            GalNet.Diagnostics.StartupBenchmark.Complete(false);
#endif
            Console.Error.WriteLine(exception);
            Environment.ExitCode = 1;
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
            .With(GameTypography.CreateFontManagerOptions())
            .LogToTrace();
}
