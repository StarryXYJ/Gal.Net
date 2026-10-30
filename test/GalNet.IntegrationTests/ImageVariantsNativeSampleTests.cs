using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Assets;
using GalNet.Core.Variable;
using GalNet.Debugging;
using GalNet.Editor.Shared.Services;
using GalNet.GameHost;
using GalNet.Runtime.Engine;
using GalNet.Sample.Player.Presentation;
using GalNet.Storage.FileSystem;

namespace GalNet.IntegrationTests;

public sealed class ImageVariantsNativeSampleTests
{
    [Test, Explicit("Starts the native Sample, installs a real package and captures rendered variant checkpoints; requires a desktop.")]
    public async Task Packaged_variants_render_in_native_sample_and_close_cleanly()
    {
        var root = Path.Combine(Path.GetTempPath(), "galnet-variants-native-" + Guid.NewGuid().ToString("N"));
        var title = "GalNet variants P5 " + Guid.NewGuid().ToString("N");
        var screenshots = SampleScreenshotOutput.GetHostDirectory(title);
        Process? app = null;
        try
        {
            var repository = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (repository is not null && !Directory.Exists(Path.Combine(repository.FullName, "GameTestCase"))) repository = repository.Parent;
            var sample = Path.Combine(repository!.FullName, "GameTestCase");
            var evidence = Path.Combine(repository.FullName, "artifacts", "image-variants-p5", "native", title);
            Directory.CreateDirectory(evidence);
            var project = Path.Combine(root, "project");
            foreach (var file in Directory.GetFiles(Path.Combine(sample, "Assets"), "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(project, Path.GetRelativePath(sample, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
            Directory.CreateDirectory(Path.Combine(project, "Graph", "groups"));
            File.Copy(Path.Combine(sample, "settings.json"), Path.Combine(project, "settings.json"));
            await File.WriteAllTextAsync(Path.Combine(project, "Graph", "graph.json"), JsonSerializer.Serialize(new
            {
                version = 2,
                name = title,
                rootNodeId = "main",
                nodes = new[] { new { id = "main", type = "Group", name = "Variants", file = "groups/main.rawgalgroup" } },
                edges = Array.Empty<object>(),
                playerVariables = Array.Empty<object>(),
                saveVariables = Array.Empty<object>()
            }));
            var source = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(sample, "Graph", "groups", "intro.rawgalgroup")))!["entries"]!.AsArray();
            var entries = new JsonArray();
            void Entry(string id) => entries.Add(source.Single(entry => entry!["id"]!.GetValue<string>() == id)!.DeepClone());
            void Capture(string name) => entries.Add(JsonSerializer.SerializeToNode(new
            { id = name, type = "sample.screenshot", parameters = new { fileName = name + ".png" } }));
            Entry("background"); Entry("portrait"); Capture("exact");
            Entry("variants-shy"); Entry("variants-solo"); Capture("reduced");
            Entry("variants-return"); Entry("classroom-speech"); Capture("default");
            Entry("classroom-teacher"); Capture("scene");
            Entry("variants-hide"); Entry("variants-reshow"); Capture("reset");
            entries.Add(JsonSerializer.SerializeToNode(new { id = "end", type = "dialogue.text", parameters = new { content = "variants.end" } }));
            entries.Insert(0, JsonSerializer.SerializeToNode(new { id = "start", type = "dialogue.text", parameters = new { content = "variants.end" } }));
            Directory.CreateDirectory(Path.Combine(project, "I18n", "en-US"));
            await File.WriteAllTextAsync(Path.Combine(project, "I18n", "en-US", "main.json"), """{"variants.end":"Variants captured."}""");
            await File.WriteAllTextAsync(Path.Combine(project, "Graph", "groups", "main.rawgalgroup"),
                new JsonObject { ["version"] = 2, ["kind"] = "Raw", ["entries"] = entries }.ToJsonString());
            var types = BuiltinResourceTypes.CreateCatalog();
            var export = await GamePackageExporter.ExportAsync("variants-native", title, project, Path.Combine(root, "package"),
                types, BuiltinGalleryTypes.CreateCatalog(types), GalNet.Project.Contracts.ProjectSchemas.CreateCatalog());
            Assert.That(export.Success, Is.True, export.Error);
            var installation = await GameInstallation.OpenAsync(export.PackagePath!);
            // Remove the entire temporary author project before starting the native host.
            Directory.Delete(project, true);
            var profile = Path.Combine(root, "profile");
            using (var store = new FilePlayerVariableStore(profile))
            {
                var count = new Variable { Name = PlayerStartup.LaunchCountVariable }; count.SetValue(1);
                var locale = new Variable { Name = PlayerPreferences.LocaleVariable }; locale.SetValue("en-US");
                await store.SaveAsync(new Dictionary<string, Variable> { [count.Name] = count, [locale.Name] = locale });
            }
            var contract = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(GalNet.Project.Contracts.ProjectSchemas).Assembly.Location)));
            using var listener = new DebugListener(new("variants-native", DebugContentIdentity.Compute(installation.RootDirectory), contract));
            var invitation = listener.Invitation;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var accept = listener.AcceptAsync(timeout.Token);
            var assembly = typeof(GalNet.Sample.Avalonia.App).Assembly.Location;
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(assembly)!
            };
            foreach (var argument in new[] { assembly, installation.RootDirectory, "--profile", profile,
                "--debug-port", invitation.Port.ToString(CultureInfo.InvariantCulture), "--debug-token", invitation.Token,
                "--debug-session", invitation.SessionId, "--debug-project", invitation.Identity.ProjectId, "--debug-contract", contract })
                start.ArgumentList.Add(argument);
            app = Process.Start(start)!;
            var stdout = app.StandardOutput.ReadToEndAsync();
            var stderr = app.StandardError.ReadToEndAsync();
            await using var peer = await accept;
            var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            await peer.RequestAsync("start", cancellationToken: timeout.Token);
            var run = (await peer.RequestAsync("status", cancellationToken: timeout.Token)).Deserialize<DebugHostStatus>(json)!.Run!;
            await peer.RequestAsync("seek", new DebugCommand("seek", run.RunId, run.Generation,
                JsonSerializer.SerializeToElement(new GameDebugSeek("main", 1, [], TargetEnd: 3), json)), timeout.Token);
            run = (await peer.RequestAsync("status", cancellationToken: timeout.Token)).Deserialize<DebugHostStatus>(json)!.Run!;
            async Task Step(int begin, int end) => await peer.RequestAsync("step",
                new DebugCommand("step", run.RunId, run.Generation, JsonSerializer.SerializeToElement(new GameDebugSpan("main", begin, end), json)), timeout.Token);
            var next = 1;
            var frameHashes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (index, name) in new[] { (3, "exact"), (6, "reduced"), (9, "default"), (11, "scene"), (14, "reset") })
            {
                await Step(next, index);
                await Task.Delay(500, timeout.Token); // Let asynchronous bitmap loading and native layout finish.
                await Step(index, index + 1);
                var image = Path.Combine(screenshots, name + ".png");
                for (var attempt = 0; attempt < 50 && !File.Exists(image); attempt++)
                    await Task.Delay(100, timeout.Token);
                Assert.That(File.Exists(image), Is.True, image);
                Assert.That(new FileInfo(image).Length, Is.GreaterThan(100_000), "Expected a rendered scene, not an empty frame.");
                Assert.That(frameHashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(image)))),
                    Is.True, "Each checkpoint should render a different expression or background.");
                var saved = Path.Combine(evidence, name + ".png");
                File.Copy(image, saved);
                TestContext.AddTestAttachment(saved, name);
                next = index + 1;
            }
            app.Refresh();
            TestContext.Out.WriteLine(JsonSerializer.Serialize(new
            { nativeWorkingSetBytes = app.WorkingSet64, nativePrivateBytes = app.PrivateMemorySize64, nativePeakWorkingSetBytes = app.PeakWorkingSet64 }));
            NativeSampleWindow.Close(app);
            await app.WaitForExitAsync(timeout.Token).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(app.ExitCode, Is.Zero, await stderr);
            TestContext.Out.WriteLine(await stdout);
        }
        catch
        {
            if (Directory.Exists(root))
                foreach (var log in Directory.GetFiles(root, "game-*.log", SearchOption.AllDirectories))
                {
                    await using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    TestContext.Out.WriteLine(await reader.ReadToEndAsync());
                }
            throw;
        }
        finally
        {
            if (app is { HasExited: false }) { app.Kill(entireProcessTree: true); await app.WaitForExitAsync(); }
            app?.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(screenshots)) Directory.Delete(screenshots, true);
        }
    }
}
