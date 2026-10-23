using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Variable;
using GalNet.Debugging;
using GalNet.Editor.Shared.Services;
using GalNet.GameHost;
using GalNet.Runtime.Engine;
using GalNet.Storage.FileSystem;

namespace GalNet.IntegrationTests;

public sealed class ExternalSampleDebugTests
{
    [Test]
    public async Task FailedSampleLaunchReportsContractErrorAndExitCodeToEditor()
    {
        var root = Path.Combine(Path.GetTempPath(), "galnet-launch-error-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var client = new ExternalGameDebugClient();
            client.Listen(new("project", "content", "different-contract"));
            var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            client.Changed += () => { if (client.Error is not null) changed.TrySetResult(); };
            var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            if (!Path.IsPathRooted(dotnet))
                dotnet = Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            client.Launch(new(dotnet, [typeof(GalNet.Sample.Avalonia.GameLaunchOptions).Assembly.Location]), root, Path.Combine(root, "profile"));
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Multiple(() =>
            {
                Assert.That(client.Status, Is.EqualTo("Application exited"));
                Assert.That(client.Error, Does.Contain("exit code 1").And.Contain("App contract differs"));
                Assert.That(client.IsConnected, Is.False);
            });
            await client.DisconnectAsync();
            client.Listen(new("next-project", "next-content"));
            Assert.That(client.Error, Is.Null);
        }
        finally { Directory.Delete(root, true); }
    }
    [Test]
    public void LaunchChecksTheActualContractAssemblyInsteadOfTrustingTheExpectedArgument()
    {
        var root = Path.Combine(Path.GetTempPath(), "galnet-debug-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(GalNet.Project.Contracts.ProjectSchemas).Assembly.Location)));
            string[] Args(string fingerprint) => [root, "--debug-port", "10000", "--debug-token", "token", "--debug-session", "session", "--debug-project", "project", "--debug-contract", fingerprint];
            var error = Assert.Throws<InvalidDataException>(() => GalNet.Sample.Avalonia.GameLaunchOptions.Parse(Args("different")));
            Assert.That(error!.Message, Does.Contain("Expected SHA256: different").And.Contain($"actual SHA256: {actual}"));
            Assert.That(GalNet.Sample.Avalonia.GameLaunchOptions.Parse(Args(actual)).DebugInvitation!.Identity.ContractFingerprint, Is.EqualTo(actual));
        }
        finally { Directory.Delete(root, true); }
    }
    [TestCase(true, false), TestCase(false, false), TestCase(false, true), Explicit("Launches the native Avalonia Sample in a separate process; requires a desktop and native media dependencies.")]
    public async Task ExternalSampleConnectsAtTitleStartsEditsSeeksStopsAndRejectsOldRuns(bool stopBeforeClose, bool withTimeline)
    {
        var root = Path.Combine(Path.GetTempPath(), "galnet-external-debug-" + Guid.NewGuid().ToString("N"));
        var content = Path.Combine(root, "Output", "Debug");
        var profile = Path.Combine(root, "profile");
        Process? app = null;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Graph", "groups"));
            Directory.CreateDirectory(Path.Combine(root, "I18n", "zh-CN"));
            await File.WriteAllTextAsync(Path.Combine(root, "Graph", "graph.json"), """
                {"version":2,"name":"Debug integration","rootNodeId":"main","nodes":[{"id":"main","type":"Group","name":"Main","file":"groups/main.rawgalgroup"}],"edges":[],"playerVariables":[],"saveVariables":[]}
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "Graph", "groups", "main.rawgalgroup"), """
                {"version":2,"kind":"Raw","entries":[{"id":"line","type":"dialogue.text","parameters":{"content":"line"}},{"id":"second","type":"dialogue.text","condition":"false","parameters":{"content":"second"}}]}
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "I18n", "zh-CN", "main.json"), """{"line":"Cross process test","second":"Conditional target"}""");
            if (withTimeline)
            {
                var groupPath = Path.Combine(root, "Graph", "groups", "main.rawgalgroup");
                var group = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(groupPath))!;
                var entries = group["entries"]!.AsArray();
                entries.Insert(0, System.Text.Json.Nodes.JsonNode.Parse("""{"id":"layer","type":"layer.showColor","parameters":{"handleId":"background","opacity":1,"transform":{},"z":0}}"""));
                entries.Insert(1, System.Text.Json.Nodes.JsonNode.Parse("""{"id":"timeline","type":"timeline.play","parameters":{"timeline":{"playbackHandleId":"motion","frameRate":60,"durationFrames":600,"tracks":[{"handleId":"background","property":"transform.x","blendMode":"Replace","keys":[{"frame":0,"value":0,"interpolationToNext":"Linear"},{"frame":600,"value":100}]}],"events":[]}}}"""));
                await File.WriteAllTextAsync(groupPath, group.ToJsonString());
            }
            await new ProjectContentBuilder(new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules())).BuildAsync(root, content);
            using (var store = new FilePlayerVariableStore(profile))
            {
                var count = new Variable { Name = PlayerStartup.LaunchCountVariable }; count.SetValue(1);
                var locale = new Variable { Name = PlayerPreferences.LocaleVariable }; locale.SetValue("zh-CN");
                await store.SaveAsync(new Dictionary<string, Variable> { [count.Name] = count, [locale.Name] = locale });
            }
            var contract = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(GalNet.Project.Contracts.ProjectSchemas).Assembly.Location)));
            using var listener = new DebugListener(new("sample-test", DebugContentIdentity.Compute(content), contract));
            var invitation = listener.Invitation;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var accept = listener.AcceptAsync(timeout.Token);
            var assembly = typeof(GalNet.Sample.Avalonia.App).Assembly.Location;
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(assembly)! };
            foreach (var arg in new[] { assembly, content, "--profile", profile, "--debug-port", invitation.Port.ToString(CultureInfo.InvariantCulture),
                "--debug-token", invitation.Token, "--debug-session", invitation.SessionId, "--debug-project", invitation.Identity.ProjectId,
                "--debug-contract", invitation.Identity.ContractFingerprint! }) start.ArgumentList.Add(arg);
            app = Process.Start(start)!;
            var stdout = app.StandardOutput.ReadToEndAsync();
            var stderr = app.StandardError.ReadToEndAsync();
            await using var peer = await accept;
            var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            async Task<DebugHostStatus> Status() => (await peer.RequestAsync("status", cancellationToken: timeout.Token)).Deserialize<DebugHostStatus>(json)!;
            async Task<JsonElement> Command(DebugRunInfo run, string method, object? args = null) =>
                await peer.RequestAsync(method, new DebugCommand(method, run.RunId, run.Generation, JsonSerializer.SerializeToElement(args, json)), timeout.Token);
            Assert.That((await Status()).Run, Is.Null);
            Assert.That((await Status()).CanStart, Is.True);
            await peer.RequestAsync("start", cancellationToken: timeout.Token);
            var first = (await Status()).Run!;
            var state = (await Command(first, "inspect")).Deserialize<GameDebugState>(json)!;
            var countValue = state.Variables.Single(variable => variable.Scope == VariableScope.Player && variable.Name.EndsWith("galnet.startup.launchCount", StringComparison.Ordinal));
            await Command(first, "setVariable", new DebugVariableEdit(countValue, "9"));
            var targetIndex = withTimeline ? 3 : 1;
            await Command(first, "seek", new GameDebugSeek("main", targetIndex, [], TargetEnd: targetIndex + 1));
            var current = (await Status()).Run!;
            var target = (await Command(current, "inspect")).Deserialize<GameDebugState>(json)!;
            Assert.That(target.Paused, Is.True);
            Assert.That(target.EntryIndex, Is.EqualTo(targetIndex));
            Assert.That(target.Variables.Single(variable => variable.Uid == countValue.Uid).Value, Is.EqualTo("9"));
            await Command(current, "step", new GameDebugSpan("main", targetIndex, targetIndex + 1));
            Assert.That((await Command(current, "inspect")).Deserialize<GameDebugState>(json)!.EntryIndex, Is.EqualTo(targetIndex + 1));
            await Command(current, "stop");
            Assert.That((await Status()).Run, Is.Null);
            await peer.RequestAsync("start", cancellationToken: timeout.Token);
            Assert.That((await Status()).Run!.RunId, Is.Not.EqualTo(first.RunId));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await Command(first, "resume"));
            if (stopBeforeClose) await Command((await Status()).Run!, "stop");
            Assert.That(app.HasExited, Is.False);
            using var pollingCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            var polling = Task.Run(async () =>
            {
                try
                {
                    while (!pollingCancellation.IsCancellationRequested)
                    {
                        var host = (await peer.RequestAsync("status", cancellationToken: pollingCancellation.Token)).Deserialize<DebugHostStatus>(json)!;
                        if (host.Run is { } run)
                        {
                            try { await peer.RequestAsync("inspect", new DebugCommand("inspect", run.RunId, run.Generation, JsonSerializer.SerializeToElement(new { }, json)), pollingCancellation.Token); }
                            catch (InvalidOperationException) { }
                        }
                        await Task.Delay(150, pollingCancellation.Token);
                    }
                }
                catch (Exception error) when (error is OperationCanceledException or IOException) { }
            });
            await Task.Delay(300, timeout.Token);
            app.CloseMainWindow();
            try { await app.WaitForExitAsync(timeout.Token).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException)
            {
                TestContext.Out.WriteLine(await File.ReadAllTextAsync(Directory.GetFiles(Path.Combine(profile, "logs"), "game-*.log").Single()));
                throw;
            }
            finally { await pollingCancellation.CancelAsync(); await polling; }
            TestContext.Out.WriteLine(await stdout);
            var errors = await stderr;
            Assert.That(app.ExitCode, Is.Zero, errors);
            using var persisted = new FilePlayerVariableStore(profile);
            Assert.That((await persisted.LoadAsync())[PlayerStartup.LaunchCountVariable].AsInt(), Is.EqualTo(9));
        }
        finally
        {
            if (app is { HasExited: false }) { app.Kill(entireProcessTree: true); await app.WaitForExitAsync(); }
            app?.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
