using System.Diagnostics;
using GalNet.Core.Variable;
using GalNet.GameHost;
using GalNet.Storage.FileSystem;

namespace GalNet.IntegrationTests;

public sealed class ComposedDistributionNativeTests
{
    [Test, Explicit("Starts the composed self-contained native Sample; set GALNET_COMPOSE_SMOKE_ROOT to its extracted directory.")]
    public async Task ComposedSampleStartsWithoutSdkDiscoveryAndClosesNormally()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("The first compose release supports win-x64.");
        var root = Environment.GetEnvironmentVariable("GALNET_COMPOSE_SMOKE_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Ignore("Set GALNET_COMPOSE_SMOKE_ROOT to a composed developer package.");
        root = Path.GetFullPath(root!);
        var evidence = Path.Combine(Path.GetDirectoryName(root)!, "native-evidence", Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(evidence, "profile");
        Directory.CreateDirectory(evidence);
        using (var store = new FilePlayerVariableStore(profile))
        {
            var count = new Variable { Name = PlayerStartup.LaunchCountVariable }; count.SetValue(1);
            var locale = new Variable { Name = PlayerPreferences.LocaleVariable }; locale.SetValue("en-US");
            await store.SaveAsync(new Dictionary<string, Variable> { [count.Name] = count, [locale.Name] = locale });
        }
        var layout = ApplicationPackageLayout.Read(root);
        var start = new ProcessStartInfo(Path.Combine(root, layout.EntryPoint))
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = evidence
        };
        start.ArgumentList.Add("--profile"); start.ArgumentList.Add(profile);
        start.Environment["PATH"] = root + Path.PathSeparator + Environment.GetFolderPath(Environment.SpecialFolder.System);
        start.Environment["DOTNET_ROOT"] = Path.Combine(root, layout.ManagedDirectory);
        start.Environment["DOTNET_ROOT_X64"] = start.Environment["DOTNET_ROOT"];
        start.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        using var app = Process.Start(start)!;
        var stdout = app.StandardOutput.ReadToEndAsync(); var stderr = app.StandardError.ReadToEndAsync();
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(45);
            var ready = false;
            while (!app.HasExited && DateTime.UtcNow < deadline)
            {
                var logs = Path.Combine(profile, "logs");
                if (Directory.Exists(logs) && Directory.GetFiles(logs, "game-*.log").Any(path =>
                    ReadActiveLog(path).Contains("Session initialized. SaveSlots=", StringComparison.Ordinal)))
                { ready = true; break; }
                await Task.Delay(200);
            }
            Assert.That(ready, Is.True, "Composed Sample did not initialize; inspect native-evidence logs.");
            var runtime = app.Modules.Cast<ProcessModule>().Single(module => module.ModuleName.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase));
            Assert.That(Path.GetDirectoryName(runtime.FileName), Is.EqualTo(Path.Combine(root, layout.ManagedDirectory)).IgnoreCase);
            var vlc = app.Modules.Cast<ProcessModule>().Single(module => module.ModuleName.Equals("libvlc.dll", StringComparison.OrdinalIgnoreCase));
            Assert.That(Path.GetDirectoryName(vlc.FileName), Is.EqualTo(Path.Combine(root, layout.ManagedDirectory, "libvlc", "win-x64")).IgnoreCase);
            NativeSampleWindow.Close(app);
            await app.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.That(app.ExitCode, Is.Zero);
        }
        finally
        {
            if (!app.HasExited) { app.Kill(entireProcessTree: true); await app.WaitForExitAsync(); }
            await File.WriteAllTextAsync(Path.Combine(evidence, "stdout.log"), await stdout);
            await File.WriteAllTextAsync(Path.Combine(evidence, "stderr.log"), await stderr);
        }
    }

    private static string ReadActiveLog(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
