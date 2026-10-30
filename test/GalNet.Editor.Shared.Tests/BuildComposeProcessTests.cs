using System.Diagnostics;
using System.Xml.Linq;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor.Shared;

public sealed class BuildComposeProcessTests
{
    private string _root = null!;
    [SetUp] public void Setup() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "GalNet-build-process-" + Guid.NewGuid().ToString("N")));
    [TearDown] public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Test]
    public async Task CapturesTheResolvedSdkAndWritesBothLogs()
    {
        var stdout = Path.Combine(_root, "stdout.log"); var stderr = Path.Combine(_root, "stderr.log");
        var result = await new BuildComposeProcessRunner().RunAsync(_root, ["--version"], stdout, stderr, CancellationToken.None);
        Assert.That(result.ExitCode, Is.Zero);
        Assert.That(result.OutputTail.Trim(), Does.Match(@"^\d+\.\d+\.\d+"));
        Assert.That(await File.ReadAllTextAsync(stdout), Does.Contain(result.OutputTail.Trim()));
        Assert.That(File.Exists(stderr), Is.True);
    }

    [Test]
    public async Task CancellationTerminatesTheOwnedMsBuildChildTree()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("Windows native process-tree regression; Linux orchestration is verified separately.");
        var marker = Path.Combine(_root, "child.pid");
        var project = Path.Combine(_root, "wait.proj");
        var command = "powershell.exe -NoProfile -Command \"[IO.File]::WriteAllText('" + marker.Replace("'", "''") + "', [string]$PID); Start-Sleep -Seconds 30\"";
        new XDocument(new XElement("Project", new XElement("Target", new XAttribute("Name", "Wait"),
            new XElement("Exec", new XAttribute("Command", command))))).Save(project);
        using var cancellation = new CancellationTokenSource();
        var operation = new BuildComposeProcessRunner().RunAsync(_root, ["msbuild", project, "-t:Wait", "-nologo", "-nr:false"],
            Path.Combine(_root, "stdout.log"), Path.Combine(_root, "stderr.log"), cancellation.Token);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while ((!File.Exists(marker) || new FileInfo(marker).Length == 0) && DateTime.UtcNow < deadline && !operation.IsCompleted) await Task.Delay(100);
            Assert.That(File.Exists(marker), Is.True, "Child process must actually start before cancellation.");
            var pid = int.Parse(await File.ReadAllTextAsync(marker), System.Globalization.CultureInfo.InvariantCulture);
            cancellation.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await operation.WaitAsync(TimeSpan.FromSeconds(10)));
            try { using var child = Process.GetProcessById(pid); Assert.That(child.HasExited, Is.True); }
            catch (ArgumentException) { /* Process no longer exists. */ }
        }
        finally
        {
            cancellation.Cancel();
            try { await operation; } catch (OperationCanceledException) { }
        }
    }
}
