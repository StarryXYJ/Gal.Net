using System;
using System.Globalization;
using Avalonia;
using Dock.Model.Core;
using Dock.Settings;
using GalNet.Editor.Services;
using Serilog;

namespace GalNet.Editor;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
#if BENCHMARK
        GalNet.Diagnostics.StartupBenchmark.Start();
#endif
        {
#if BENCHMARK
            using var measurement = GalNet.Diagnostics.CpuProfiler.Shared.Measure("Editor.Logging");
#endif
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(
                    formatProvider: CultureInfo.InvariantCulture,
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.Debug(
                    formatProvider: CultureInfo.InvariantCulture,
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.Sink(EditorLogSink.Instance)
                .CreateLogger();
        }

        try
        {
            Log.Information("GalNet Editor starting");
            using var preparation = new EditorStartupPreparation();
            AppBuilder app;
#if BENCHMARK
            using (GalNet.Diagnostics.CpuProfiler.Shared.Measure("Editor.ConfigureAvalonia"))
#endif
            app = ConfigureApp(AppBuilder.Configure(() => new App { Preparation = preparation }));
#if BENCHMARK
            GalNet.Diagnostics.CpuProfiler.Shared.Mark("Editor.RunAvalonia");
#endif
            app.StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            Environment.ExitCode = 1;
#if BENCHMARK
            GalNet.Diagnostics.StartupBenchmark.Complete(false);
#endif
        }
        finally
        {
            Log.CloseAndFlush();
#if BENCHMARK
            if (GalNet.Diagnostics.StartupBenchmark.Failed) Environment.ExitCode = 1;
#endif
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => ConfigureApp(AppBuilder.Configure<App>());

    private static AppBuilder ConfigureApp(AppBuilder builder)
        => builder
            .UsePlatformDetect()
            .UseManagedWindows(false)
            .UseFloatingWindowHostMode(DockFloatingWindowHostMode.Native)
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
