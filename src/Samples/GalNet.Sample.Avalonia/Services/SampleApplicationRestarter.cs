using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace GalNet.Sample.Avalonia.Services;

/// <summary>Restarts the Sample with the exact host command and launch arguments it received.</summary>
internal static class SampleApplicationRestarter
{
    public static bool IsRestarting { get; private set; }

    public static void Restart()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            throw new InvalidOperationException("The Sample can only restart from a desktop application lifetime.");

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            throw new InvalidOperationException("The current process path is unavailable.");

        var startInfo = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.CurrentDirectory
        };
        foreach (var argument in Environment.GetCommandLineArgs().Skip(1))
            startInfo.ArgumentList.Add(argument);

        if (Process.Start(startInfo) is null)
            throw new InvalidOperationException("Unable to start the replacement Sample process.");

        IsRestarting = true;
        desktop.Shutdown();
    }
}
