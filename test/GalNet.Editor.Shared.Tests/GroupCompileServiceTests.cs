using System.Security.Cryptography;
using System.Text.Json;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Shared.Services;
using GeneralTest.Editor.Shared;

namespace GalNet.Editor.Shared.Tests;

public sealed class GroupCompileServiceTests
{
    private string _root = null!;
    private const string Source = "Graph/groups/a.rawgalgroup";
    private const string Output = "Output/Graph/groups/a.galgroup";
    private const string Raw = """{"version":2,"kind":"Raw","entries":[{"id":"bundle","type":"custom.bundle","parameters":{}}]}""";
    [SetUp]
    public void SetUp()
    {
        _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GalNet-compile-" + Guid.NewGuid())).FullName;
        Put("Graph/graph.json", """{"version":2,"nodes":[{"id":"a","type":"Group","file":"groups/a.rawgalgroup"},{"id":"broken","type":"Group","file":"../../outside.rawgalgroup"}]}""");
        Put(Source, Raw);
        Put("Graph/groups/broken.rawgalgroup", "broken");
        Deploy("SchemaA");
    }
    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    [Test]
    public async Task Compile_ExpandsCustomComposite_OnlyPublishesTarget_AndPreservesAuthors()
    {
        Put("Output/Graph/groups/other.galgroup", "other output");
        var before = Snapshot();
        var result = await new GroupCompileService(_root).CompileAsync(new(new(Id: "a")));
        Assert.That(result.Success, Is.True, string.Join(";", result.Diagnostics));
        var report = (GroupCompileReport)result.Data!;
        Assert.Multiple(() =>
        {
            Assert.That(report.Source, Is.EqualTo(Source));
            Assert.That(report.CompiledEntryCount, Is.EqualTo(1));
            Assert.That(report.SourceMap["bundle"], Is.EqualTo(new[] { "bundle#1" }));
            Assert.That(report.ContractSha256, Has.Length.EqualTo(64));
            Assert.That(Snapshot().Where(item => !item.StartsWith(Output + ":", StringComparison.Ordinal)), Is.EqualTo(before));
        });
        using var compiled = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, Output)));
        Assert.That(compiled.RootElement.GetProperty("version").GetInt32(), Is.EqualTo(3));
        Assert.That(compiled.RootElement.GetProperty("entries")[0].GetProperty("typeId").GetString(), Is.EqualTo("custom.example"));
    }

    [Test]
    public async Task DryRun_ReturnsFullSummary_WithoutCreatingDirectoriesOrFiles()
    {
        var before = Snapshot();
        var result = await new GroupCompileService(_root).CompileAsync(new(new(Id: "a"), DryRun: true));
        Assert.That(result.Success, Is.True);
        Assert.That(((GroupCompileReport)result.Data!).DryRun, Is.True);
        Assert.That(Directory.Exists(Path.Combine(_root, "Output")), Is.False);
        Assert.That(Snapshot(), Is.EqualTo(before));
    }

    [Test]
    public async Task OrphanFile_RequiresExplicitOutput_AndCanCompileWithoutGraph()
    {
        File.Delete(Path.Combine(_root, "Graph/graph.json"));
        var service = new GroupCompileService(_root);
        var noOutput = await service.CompileAsync(new(new(File: Source)));
        Assert.That(noOutput.Diagnostics.Select(item => item.Code), Does.Contain("group.outputRequired"));
        var result = await service.CompileAsync(new(new(File: Source), "Output/orphan.galgroup"));
        Assert.That(result.Success, Is.True, string.Join(";", result.Diagnostics));
        Assert.That(File.Exists(Path.Combine(_root, "Output/orphan.galgroup")), Is.True);
    }

    [Test]
    public async Task UniqueFileOwner_UsesGroupIdAsDefaultOutput()
    {
        var result = await new GroupCompileService(_root).CompileAsync(new(new(File: Source), DryRun: true));
        Assert.That(result.Success, Is.True);
        Assert.That(((GroupCompileReport)result.Data!).Output, Is.EqualTo(Path.Combine(_root, Output.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Test]
    public async Task InvalidSource_PreservesOldArtifactAndAllAuthorBytes()
    {
        Put(Output, "old artifact");
        Put(Source, Raw.Replace("\"parameters\":{}", "\"parameters\":{\"unknown\":1}", StringComparison.Ordinal));
        var before = Snapshot();
        var result = await new GroupCompileService(_root).CompileAsync(new(new(Id: "a")));
        Assert.That(result.Diagnostics.Select(item => item.Code), Does.Contain("entry.parametersInvalid"));
        Assert.That(Snapshot(), Is.EqualTo(before));
    }

    [Test]
    public async Task MissingContract_PreservesOldArtifact_WithoutBuiltinFallback()
    {
        Put(Output, "old artifact");
        File.Delete(Path.Combine(_root, "Contracts", ProjectSchemaContext.AssemblyFileName));
        var before = Snapshot();
        var result = await new GroupCompileService(_root).CompileAsync(new(new(Id: "a")));
        Assert.That(result.Diagnostics.Select(item => item.Code), Does.Contain("contract.load"));
        Assert.That(Snapshot(), Is.EqualTo(before));
    }

    [Test]
    public async Task CancelledOperation_PreservesOldArtifact()
    {
        Put(Output, "old artifact");
        var before = Snapshot();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await new GroupCompileService(_root).CompileAsync(new(new(Id: "a")), cancellation.Token);
        Assert.That(result.Diagnostics.Select(item => item.Code), Does.Contain("group.compileCancelled"));
        Assert.That(Snapshot(), Is.EqualTo(before));
    }

    [TestCase("Graph/compiled.galgroup")]
    [TestCase("Assets/resource.galgroup")]
    [TestCase("Contracts/schema.galgroup")]
    [TestCase("Output/a.rawgalgroup")]
    public async Task OutputConflicts_DoNotModifyAuthorFiles(string output)
    {
        var before = Snapshot();
        var result = await new GroupCompileService(_root).CompileAsync(new(new(Id: "a"), output));
        Assert.That(result.Success, Is.False);
        Assert.That(Snapshot(), Is.EqualTo(before));
    }

    [Test]
    public async Task PublicationFailure_PreservesOldArtifactAndRemovesTemporaryFile()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("Windows file sharing lock behavior.");
        Put(Output, "old artifact");
        using (var locked = new FileStream(Path.Combine(_root, Output), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = await new GroupCompileService(_root).CompileAsync(new(new(Id: "a")));
            Assert.That(result.Success, Is.False);
        }
        Assert.That(File.ReadAllText(Path.Combine(_root, Output)), Is.EqualTo("old artifact"));
        Assert.That(Directory.GetFiles(Path.GetDirectoryName(Path.Combine(_root, Output))!, "*.tmp"), Is.Empty);
    }

    [Test]
    public async Task LinkedOutput_IsRejectedAndExternalDirectoryUnchanged()
    {
        var external = Directory.CreateDirectory(Path.Combine(_root, "external")).FullName;
        var link = Path.Combine(_root, "Output");
        DirectoryLinks.Create(link, external);
        try
        {
            var result = await new GroupCompileService(_root).CompileAsync(new(new(Id: "a")));
            Assert.That(result.Success, Is.False);
            Assert.That(Directory.GetFiles(external, "*", SearchOption.AllDirectories), Is.Empty);
        }
        finally { Directory.Delete(link); }
    }

    [Test]
    public async Task ActualCli_HelpAndCompilationUseSameRoute()
    {
        var help = await CliHelpTests.Invoke(["help", "group", "compile", "--json"]);
        Assert.That(help.ExitCode, Is.Zero);
        Assert.That(help.Output, Does.Contain("--dry-run").And.Contain("compiledEntryCount"));
        var result = await CliHelpTests.Invoke(["group", "compile", _root, "--id", "a"]);
        Assert.That(result.ExitCode, Is.Zero, result.Output);
        Assert.That(File.Exists(Path.Combine(_root, Output)), Is.True);
    }

    private void Deploy(string fixture)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "GalNet.slnx"))) directory = directory.Parent!;
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var destination = Directory.CreateDirectory(Path.Combine(_root, "Contracts")).FullName;
        foreach (var file in Directory.GetFiles(Path.Combine(directory.FullName, "test", "Fixtures", fixture, "bin", configuration, "net10.0")))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
    }
    private void Put(string file, string text)
    { var path = Path.Combine(_root, file); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
    private string[] Snapshot() => Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(_root, file).Replace('\\', '/') + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))).Order(StringComparer.Ordinal).ToArray();
}
