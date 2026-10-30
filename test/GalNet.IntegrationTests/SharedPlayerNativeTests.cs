using System.Diagnostics;
using GalNet.Core.Variable;
using GalNet.GameHost;
using GalNet.Storage.FileSystem;

namespace GalNet.IntegrationTests;

public sealed class SharedPlayerNativeTests
{
    [TestCase("legacy", "GalNet.Sample.Avalonia.exe")]
    [TestCase("cross", "GalNet.Sample.CrossPlatform.Desktop.exe")]
    [Explicit("Starts the source-published original and CrossPlatform players. Set GALNET_SHARED_PLAYER_NATIVE_ROOT.")]
    public async Task SharedSurfaceStartsBothNativeDesktopHostsAndConfirmsNormalExit(string role, string entry)
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("This probe validates Windows native hosts.");
        var selected = Environment.GetEnvironmentVariable("GALNET_SHARED_PLAYER_NATIVE_ROOT");
        if (string.IsNullOrWhiteSpace(selected)) Assert.Ignore("Set GALNET_SHARED_PLAYER_NATIVE_ROOT to the native evidence root.");
        var root = Path.GetFullPath(selected!);
        var appRoot = Path.Combine(root, role);
        var evidence = Path.Combine(root, "runs", role + "-" + Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(evidence, "profile");
        Directory.CreateDirectory(evidence);
        using (var store = new FilePlayerVariableStore(profile))
        {
            var count = new Variable { Name = PlayerStartup.LaunchCountVariable }; count.SetValue(1);
            var locale = new Variable { Name = PlayerPreferences.LocaleVariable }; locale.SetValue("en-US");
            await store.SaveAsync(new Dictionary<string, Variable> { [count.Name] = count, [locale.Name] = locale });
        }
        var start = new ProcessStartInfo(Path.Combine(appRoot, entry))
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = evidence
        };
        start.ArgumentList.Add(Path.Combine(root, "content"));
        start.ArgumentList.Add("--profile"); start.ArgumentList.Add(profile);
        start.Environment["PATH"] = appRoot + Path.PathSeparator + Environment.GetFolderPath(Environment.SpecialFolder.System);
        start.Environment["DOTNET_ROOT"] = Path.Combine(evidence, "no-sdk");
        start.Environment["DOTNET_ROOT_X64"] = start.Environment["DOTNET_ROOT"];
        start.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        using var app = Process.Start(start)!;
        var stdout = app.StandardOutput.ReadToEndAsync();
        var stderr = app.StandardError.ReadToEndAsync();
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(45);
            var ready = false;
            while (!app.HasExited && DateTime.UtcNow < deadline)
            {
                if (Directory.Exists(Path.Combine(profile, "logs")) && Directory.GetFiles(Path.Combine(profile, "logs"), "game-*.log")
                    .Any(path => ReadLog(path).Contains("Session initialized. SaveSlots=", StringComparison.Ordinal)))
                { ready = true; break; }
                await Task.Delay(100);
            }
            Assert.That(ready, Is.True, evidence);
            var modules = app.Modules.Cast<ProcessModule>().ToArray();
            foreach (var filename in new[] { "coreclr.dll", "SDL3.dll" })
                Assert.That(Path.GetDirectoryName(modules.Single(module => module.ModuleName.Equals(filename, StringComparison.OrdinalIgnoreCase)).FileName),
                    Is.EqualTo(appRoot).IgnoreCase);
            Assert.That(Path.GetDirectoryName(modules.Single(module => module.ModuleName.Equals("libvlc.dll", StringComparison.OrdinalIgnoreCase)).FileName),
                Is.EqualTo(Path.Combine(appRoot, "libvlc", "win-x64")).IgnoreCase);
            NativeSampleWindow.Close(app);
            await app.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.That(app.ExitCode, Is.Zero);
            using var restored = new FilePlayerVariableStore(profile);
            Assert.That((await restored.LoadAsync())[PlayerStartup.LaunchCountVariable].AsInt(), Is.EqualTo(2));
        }
        finally
        {
            if (!app.HasExited) { app.Kill(entireProcessTree: true); await app.WaitForExitAsync(); }
            await File.WriteAllTextAsync(Path.Combine(evidence, "stdout.log"), await stdout);
            await File.WriteAllTextAsync(Path.Combine(evidence, "stderr.log"), await stderr);
        }
    }

    private static string ReadLog(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
