using System.Diagnostics;
using System.Text.Json;

namespace GeneralTest.Editor.Shared;

public sealed class CliHelpTests
{
    [TestCase("help", "--json")]
    [TestCase("help", "--search", "group", "--json")]
    [TestCase("help", "graph.node.create", "--json")]
    [TestCase("help", "graph", "nodes", "--json")]
    public async Task JsonHelp_WorksWithoutAProject_AndHasCompleteDescriptors(params string[] arguments)
    {
        var (exitCode, stdout, stderr) = await Invoke(arguments);
        using var parsed = JsonDocument.Parse(stdout);
        var root = parsed.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Zero, stderr);
            Assert.That(root.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("operationId").GetString(), Is.EqualTo("help"));
            Assert.That(root.GetProperty("protocolVersion").GetInt32(), Is.EqualTo(1));
        });
        foreach (var definition in root.GetProperty("data").GetProperty("operations").EnumerateArray())
            Assert.Multiple(() =>
            {
                Assert.That(definition.TryGetProperty("inputSchema", out _), Is.True);
                Assert.That(definition.TryGetProperty("outputSchema", out _), Is.True);
                Assert.That(definition.TryGetProperty("supportsDryRun", out _), Is.True);
                Assert.That(definition.TryGetProperty("exitCodes", out _), Is.True);
            });
    }

    [TestCase("project", "delete", "project")]
    [TestCase("commands")]
    [TestCase("nodes", "project")]
    [TestCase("help", "unknown")]
    [TestCase("help", "--unexpected")]
    [TestCase("graph", "nodes", "project", "--unexpected")]
    public async Task InvalidCommands_ReturnJsonAndUsageExitCode(params string[] arguments)
    {
        var (exitCode, stdout, _) = await Invoke(arguments);
        using var parsed = JsonDocument.Parse(stdout);
        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(2));
            Assert.That(parsed.RootElement.GetProperty("success").GetBoolean(), Is.False);
            Assert.That(parsed.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("cli.arguments"));
        });
    }

    internal static async Task<(int ExitCode, string Output, string Error)> Invoke(string[] arguments)
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "GalNet.slnx"))) root = root.Parent!;
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var dll = Path.Combine(root.FullName, "src", "Editor", "GalNet.Editor.Headless", "bin", configuration, "net10.0", "GalNet.Editor.Headless.dll");
        // Isolated --artifacts-path builds place the CLI beside this test project.
        var sibling = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "GalNet.Editor.Headless", new DirectoryInfo(TestContext.CurrentContext.TestDirectory).Name, "GalNet.Editor.Headless.dll"));
        if (File.Exists(sibling)) dll = sibling;
        var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(dll);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output, await error);
    }
}
