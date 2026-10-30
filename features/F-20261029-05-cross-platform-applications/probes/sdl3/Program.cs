using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace GalNet.Probes.Sdl3;

internal static class Program
{
    internal static string NativeManifest { get; private set; } = "";
    internal static string ResultPath { get; private set; } = "";
    internal static int ExitCode { get; set; } = 1;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: Sdl3Probe <absolute native.json> <absolute result.json>");
            return 2;
        }
        NativeManifest = Path.GetFullPath(args[0]);
        ResultPath = Path.GetFullPath(args[1]);
        try
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(NativeManifest));
            var metadata = manifest.RootElement;
            if (metadata.GetProperty("version").GetString() != "3.4.18")
                throw new InvalidDataException("This probe pins SDL 3.4.18.");
            var target = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
                metadata.GetProperty("target").GetString() != target)
                throw new InvalidDataException("Native target must match the x64 probe process.");
            var filename = metadata.GetProperty("library").GetString()!;
            if (filename != Path.GetFileName(filename)) throw new InvalidDataException("Native filename only.");
            var library = Path.Combine(Path.GetDirectoryName(NativeManifest)!, filename);
            using var file = File.OpenRead(library);
            var hash = Convert.ToHexStringLower(SHA256.HashData(file));
            if (hash != metadata.GetProperty("sha256").GetString())
                throw new InvalidDataException("Native library hash mismatch.");
            // Exact path, no machine-wide SDL fallback. Keep loaded until process exit.
            var handle = NativeLibrary.Load(library);
            NativeLibrary.SetDllImportResolver(typeof(SdlNative).Assembly,
                (name, _, _) => name == "galnet-sdl3-probe" ? handle : 0);
            AppBuilder.Configure<ProbeApp>().UsePlatformDetect().WithInterFont()
                .With(new global::Avalonia.Media.FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" })
                .StartWithClassicDesktopLifetime([]);
        }
        catch (Exception exception)
        {
            ExitCode = 1;
            Console.Error.WriteLine(exception);
            File.WriteAllText(ResultPath, JsonSerializer.Serialize(new { passed = false, error = exception.ToString() }));
        }
        return ExitCode;
    }
}

internal sealed class ProbeApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new Window
            {
                Title = "GalNet SDL3 input probe", Width = 440, Height = 160,
                Content = new TextBlock { Text = "SDL3 input probe — closes automatically", Margin = new Thickness(20) }
            };
            desktop.MainWindow = window;
            window.Opened += async (_, _) =>
            {
                // Opened can run synchronously inside lifetime startup. Yield
                // before the probe is allowed to shut that lifetime down.
                await Task.Delay(100);
                var run = new ProbeRun();
                try
                {
                    await run.ExecuteAsync(window);
                    Program.ExitCode = 0;
                }
                catch (Exception exception) { run.Failure = exception.ToString(); }
                finally
                {
                    run.WriteResult();
                    desktop.Shutdown(Program.ExitCode);
                }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
