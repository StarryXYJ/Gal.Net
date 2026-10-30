using System;
using Avalonia;
using GalNet.Avalonia.Themes;
using GalNet.Storage.FileSystem;

namespace GalNet.Sample.CrossPlatform.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            var content = ApplicationPackageLayout.GetContentDirectory(ApplicationPackageLayout.FindApplicationRoot(AppContext.BaseDirectory));
            string? profile = null;
            var hasContent = false;
            for (var index = 0; index < args.Length; index++)
            {
                if (args[index] == "--profile" && index + 1 < args.Length && !args[index + 1].StartsWith('-'))
                    profile = Path.GetFullPath(args[++index]);
                else if (!args[index].StartsWith('-') && !hasContent)
                { content = Path.GetFullPath(args[index]); hasContent = true; }
                else throw new ArgumentException($"Unknown or incomplete launch argument: {args[index]}");
            }
            var paths = new PlayerLaunchPaths(content, profile ?? SampleCustomization.GetProfileDirectory(content));
            App.PrepareLaunchAsync = _ => Task.FromResult(paths);
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .With(GameTypography.CreateFontManagerOptions("fonts:Inter#Inter"))
            .LogToTrace();
}
