using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace GalNet.Media.LibVlc;

/// <summary>Initializes native loading once; callers own each independent LibVLC session returned by Create.</summary>
public sealed class LibVlcFactory
{
    private static readonly object InitializationGate = new();
    private static LibVlcOptions? _initializedOptions;
    private static ExceptionDispatchInfo? _initializationFailure;
    public static LibVlcFactory Default { get; } = new(LibVlcOptions.ForCurrentDesktop());

    public LibVlcOptions Options { get; }
    public string? NativeVersion { get; private set; }
    public string? InitializationError { get; private set; }

    public LibVlcFactory(LibVlcOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options with
        {
            NativeLibraryDirectory = AbsolutePath(options.NativeLibraryDirectory),
            PluginDirectory = AbsolutePath(options.PluginDirectory)
        };
        if (Options.UseSystemLibraries == (Options.NativeLibraryDirectory is not null))
            throw new ArgumentException("Select either an explicit private LibVLC directory or system library loading.", nameof(options));
    }

    public LibVLC Create(params string[] sessionOptions)
    {
        try
        {
            EnsureInitialized();
            var instance = new LibVLC(["--no-video-title-show", .. sessionOptions]);
            NativeVersion = instance.Version;
            InitializationError = null;
            return instance;
        }
        catch (Exception error)
        {
            InitializationError = $"LibVLC initialization failed (RID={RuntimeInformation.RuntimeIdentifier}, " +
                $"native={Options.NativeLibraryDirectory ?? "system loader"}, plugins={Options.PluginDirectory ?? "system default"}): {error.Message}";
            throw new InvalidOperationException(InitializationError, error);
        }
    }

    private void EnsureInitialized()
    {
        // Validate before changing process-global state. A missing private deployment must
        // never silently fall back to an installed VLC with a different plugin ABI.
        if (Options.NativeLibraryDirectory is { } native)
        {
            if (OperatingSystem.IsLinux())
                throw new PlatformNotSupportedException("LibVLCSharp does not accept a Linux library directory. Configure the loader before process start; private Linux deployment is a separate packaging step.");
            var suffix = OperatingSystem.IsWindows() ? ".dll" : ".dylib";
            foreach (var name in new[] { "libvlc", "libvlccore" })
                if (!File.Exists(Path.Combine(native, name + suffix)))
                    throw new FileNotFoundException($"Missing private LibVLC library '{name + suffix}' in '{native}'.");
        }
        if (Options.PluginDirectory is { } plugins && !Directory.Exists(plugins))
            throw new DirectoryNotFoundException($"Missing LibVLC plugin directory '{plugins}'.");

        lock (InitializationGate)
        {
            if (_initializedOptions is not null)
            {
                if (!SameConfiguration(_initializedOptions, Options))
                    throw new InvalidOperationException("LibVLC has already been initialized with different native/plugin paths. Configure one factory in the application composition root before creating media sessions.");
                _initializationFailure?.Throw();
                return;
            }
            _initializedOptions = Options;
            var previousPlugins = Environment.GetEnvironmentVariable("VLC_PLUGIN_PATH");
            try
            {
                // LibVLC can load plugins long after initialization; keep this value for
                // the whole process rather than temporarily setting it per player.
                Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", Options.PluginDirectory);
                LibVLCSharp.Shared.Core.Initialize(Options.NativeLibraryDirectory);
            }
            catch (Exception error)
            {
                Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", previousPlugins);
                _initializationFailure = ExceptionDispatchInfo.Capture(error);
                throw;
            }
        }
    }

    private static string? AbsolutePath(string? path)
    {
        if (path is null) return null;
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("LibVLC paths must be absolute and independent of the working directory.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static bool SameConfiguration(LibVlcOptions first, LibVlcOptions second)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return first.UseSystemLibraries == second.UseSystemLibraries
            && string.Equals(first.NativeLibraryDirectory, second.NativeLibraryDirectory, comparison)
            && string.Equals(first.PluginDirectory, second.PluginDirectory, comparison);
    }
}
