using System.Diagnostics;

namespace GalNet.Sample.Avalonia.Debug;

/// <summary>Opens the physical directory containing the Sample's rolling logs.</summary>
public sealed class SampleDebugLogDirectory
{
    private readonly GameLaunchOptions _launchOptions = App.LaunchOptions;

    public void Open()
    {
        var directory = _launchOptions.LogDirectory;
        Directory.CreateDirectory(directory);
        if (Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true }) is null)
            throw new InvalidOperationException("can not open log directory");
    }
}
