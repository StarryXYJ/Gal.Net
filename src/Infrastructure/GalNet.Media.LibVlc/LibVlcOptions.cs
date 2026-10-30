using System.Runtime.InteropServices;

namespace GalNet.Media.LibVlc;

/// <summary>Process-wide native loading configuration. Explicit paths must be absolute.</summary>
public sealed record LibVlcOptions
{
    public string? NativeLibraryDirectory { get; init; }
    public string? PluginDirectory { get; init; }
    public bool UseSystemLibraries { get; init; }

    /// <summary>Linux system loading is a development baseline, not a portable distribution.</summary>
    public static LibVlcOptions ForCurrentDesktop()
    {
        if (OperatingSystem.IsLinux()) return new() { UseSystemLibraries = true };
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("Configure the mobile LibVLC platform binding in its application host.");
        var directory = Path.Combine(AppContext.BaseDirectory, "libvlc", RuntimeInformation.RuntimeIdentifier);
        return new() { NativeLibraryDirectory = directory, PluginDirectory = Path.Combine(directory, "plugins") };
    }
}
