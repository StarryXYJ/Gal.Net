using System.IO.Compression;
using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Assets;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Serialization;
using GalNet.Editor.Shared.Services;
using GalNet.Storage.FileSystem;

namespace GeneralTest.Editor.Shared;

public sealed class DistributionComposeTests
{
    private string _root = null!;
    private string Source => Path.Combine(_root, "source");
    private string Runtime => Path.Combine(_root, "runtime");
    private string Content => Path.Combine(_root, "content");
    private string Output => Path.Combine(_root, "output", "game.zip");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [SetUp]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "GalNet-compose-" + Guid.NewGuid().ToString("N"));
        var repo = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(repo.FullName, "GalNet.slnx"))) repo = repo.Parent!;
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        using var contracts = ProjectContractSource.Prepare(Path.Combine(repo.FullName, "src/Editor/GalNet.Editor.DefaultContracts/bin", configuration, "net10.0"));
        Directory.CreateDirectory(Path.Combine(Source, "Contracts"));
        Directory.CreateDirectory(Path.Combine(Runtime, "lib"));
        Directory.CreateDirectory(Path.Combine(Runtime, "docs"));
        Directory.CreateDirectory(Path.Combine(Runtime, "pak"));
        foreach (var file in Directory.GetFiles(contracts.DirectoryPath))
        {
            File.Copy(file, Path.Combine(Source, "Contracts", Path.GetFileName(file)));
            File.Copy(file, Path.Combine(Runtime, "lib", Path.GetFileName(file)));
        }
        Directory.CreateDirectory(Path.Combine(Source, "Graph/groups"));
        File.WriteAllText(Path.Combine(Source, "Graph/graph.json"), """{"version":2,"name":"Compose","rootNodeId":"main","nodes":[{"id":"main","name":"Main","type":"Group","file":"groups/main.rawgalgroup"}],"edges":[],"playerVariables":[],"saveVariables":[]}""");
        File.WriteAllText(Path.Combine(Source, "Graph/groups/main.rawgalgroup"), """{"version":2,"kind":"Raw","entries":[]}""");
        File.WriteAllText(Path.Combine(Source, "settings.json"), """{"version":1,"sections":{}}""");
        File.Copy(Environment.ProcessPath!, Path.Combine(Runtime, "Player.exe"));
        File.WriteAllText(Path.Combine(Runtime, ApplicationPackageLayout.FileName), JsonSerializer.Serialize(new ApplicationPackageLayout
        {
            RuntimeIdentifier = "win-x64", EntryPoint = "Player.exe", ManagedDirectory = "lib", ContractsDirectory = "lib", ContentDirectory = "pak"
        }, JsonOptions));
        await WriteRuntimeManifest();
        using var schema = ProjectSchemaContext.Load(Source);
        var types = BuiltinResourceTypes.CreateCatalog();
        var directory = await GamePackageExporter.ExportDirectoryAsync("compose-project", "Compose", Source, Content,
            types, BuiltinGalleryTypes.CreateCatalog(types), schema.Catalog, schema.Configurations);
        Assert.That(directory.Success, Is.True, directory.Error);
        var archive = await GamePackageExporter.ExportAsync("compose-project", "Compose", Source, Path.Combine(_root, "archives"),
            types, BuiltinGalleryTypes.CreateCatalog(types), schema.Catalog, schema.Configurations);
        Assert.That(archive.Success, Is.True, archive.Error);
    }

    [TearDown] public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task ComposesBothInputFormsAndRebuildsTheFinalHashManifest(bool runtimeZip, bool contentZip)
    {
        var runtime = Runtime;
        if (runtimeZip) { runtime = Path.Combine(_root, "runtime.zip"); ZipFile.CreateFromDirectory(Runtime, runtime); }
        var content = contentZip ? Path.Combine(_root, "archives/Compose.galpak") : Content;
        var result = await GameDistributionComposer.ComposeAsync(runtime, content, Output);
        Assert.That(result.ProjectId, Is.EqualTo("compose-project"));
        Assert.That(result.Sha256, Is.EqualTo(CryptoHelper.HashSHA256(File.ReadAllBytes(Output))));
        var extracted = Path.Combine(_root, "extracted"); ZipFile.ExtractToDirectory(Output, extracted);
        Assert.That(ApplicationPackageLayout.FindApplicationRoot(Path.Combine(extracted, "lib")), Is.EqualTo(extracted));
        var game = PackedGameDocuments.ReadGame(File.ReadAllBytes(Path.Combine(extracted, "pak/game.pak")));
        Assert.That(game.Manifest.ContractFiles.Count, Is.GreaterThanOrEqualTo(3));
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(extracted, GameDistributionComposer.ManifestPath)));
        Assert.That(manifest.RootElement.GetProperty("package").GetString(), Is.EqualTo("Sample-Game"));
        foreach (var entry in manifest.RootElement.GetProperty("files").EnumerateArray())
            Assert.That(CryptoHelper.HashSHA256(File.ReadAllBytes(Path.Combine(extracted, entry.GetProperty("path").GetString()!))),
                Is.EqualTo(entry.GetProperty("sha256").GetString()));
        Assert.That(File.Exists(Path.Combine(Runtime, "pak/game.pak")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(extracted, "pak/ResourcePatches")), Is.True);
    }

    [TestCase("hash")]
    [TestCase("contract")]
    [TestCase("undeclared-runtime")]
    [TestCase("undeclared-content")]
    [TestCase("overlap")]
    public async Task RejectsInvalidInputsWithoutPublishingOrChangingContent(string change)
    {
        var original = File.ReadAllBytes(Path.Combine(Content, "game.pak"));
        if (change is "hash" or "contract")
        {
            File.AppendAllText(Path.Combine(Runtime, "lib/GalNet.Project.Contracts.dll"), "changed");
            if (change == "contract") await WriteRuntimeManifest();
        }
        else if (change == "undeclared-runtime") File.WriteAllText(Path.Combine(Runtime, "unexpected.txt"), "extra");
        else if (change == "undeclared-content") File.WriteAllText(Path.Combine(Content, "settings.json"), "{}");
        else
        {
            var layout = JsonNode.Parse(File.ReadAllText(Path.Combine(Runtime, ApplicationPackageLayout.FileName)))!;
            layout["contentDirectory"] = "lib"; File.WriteAllText(Path.Combine(Runtime, ApplicationPackageLayout.FileName), layout.ToJsonString());
            await WriteRuntimeManifest();
        }
        Assert.ThrowsAsync<InvalidDataException>(() => GameDistributionComposer.ComposeAsync(Runtime, Content, Output));
        Assert.That(File.Exists(Output), Is.False);
        Assert.That(File.ReadAllBytes(Path.Combine(Content, "game.pak")), Is.EqualTo(original));
        Assert.That(Directory.GetDirectories(Path.GetDirectoryName(Output)!, ".compose-*"), Is.Empty);
    }

    [TestCase("../escape")]
    [TestCase("Player.exe")]
    [TestCase("link")]
    public void RejectsUnsafeRuntimeArchives(string extra)
    {
        var archive = Path.Combine(_root, "runtime.zip"); ZipFile.CreateFromDirectory(Runtime, archive);
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        {
            var entry = zip.CreateEntry(extra);
            if (extra == "link") entry.ExternalAttributes = (0xa1ff << 16);
            using var writer = new StreamWriter(entry.Open()); writer.Write("outside");
        }
        Assert.ThrowsAsync<InvalidDataException>(() => GameDistributionComposer.ComposeAsync(archive, Content, Output));
        Assert.That(File.Exists(Path.Combine(_root, "escape")), Is.False);
        Assert.That(File.Exists(Output), Is.False);
    }

    [Test]
    public void RefusesContentWithoutADeclaredContractIdentity()
    {
        var path = Path.Combine(Content, PackedGameDocuments.GameFileName);
        var packed = PackedGameDocuments.ReadGame(File.ReadAllBytes(path));
        var documents = packed.Documents.Documents.ToDictionary();
        documents[GameContentManifest.FileName] = JsonSerializer.SerializeToUtf8Bytes(packed.Manifest with { ContractFiles = [] }, JsonOptions);
        File.WriteAllBytes(path, PackedGameDocuments.Build(packed.Manifest.ProjectId, packed.Manifest.BuildId, documents));
        Assert.ThrowsAsync<InvalidDataException>(() => GameDistributionComposer.ComposeAsync(Runtime, Content, Output));
        Assert.That(File.Exists(Output), Is.False);
    }

    [Test]
    public void RefusesExistingOrNestedOutputAndCleansCanceledStages()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Output)!); File.WriteAllText(Output, "keep");
        Assert.ThrowsAsync<IOException>(() => GameDistributionComposer.ComposeAsync(Runtime, Content, Output));
        Assert.That(File.ReadAllText(Output), Is.EqualTo("keep"));
        Assert.ThrowsAsync<InvalidDataException>(() => GameDistributionComposer.ComposeAsync(Runtime, Content, Path.Combine(Runtime, "game.zip")));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(() => GameDistributionComposer.ComposeAsync(Runtime, Content, Path.Combine(_root, "canceled.zip"), canceled.Token));
        Assert.That(Directory.GetDirectories(_root, ".compose-*"), Is.Empty);
    }

    private Task WriteRuntimeManifest()
    {
        var files = Directory.GetFiles(Runtime, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetRelativePath(Runtime, path).Replace('\\', '/') != GameDistributionComposer.ManifestPath)
            .Select(path => new GalpakFileEntry(Path.GetRelativePath(Runtime, path).Replace('\\', '/'),
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), new FileInfo(path).Length));
        return File.WriteAllTextAsync(Path.Combine(Runtime, GameDistributionComposer.ManifestPath), JsonSerializer.Serialize(new
        { schemaVersion = 1, runtime = JsonNode.Parse(File.ReadAllText(Path.Combine(Runtime, ApplicationPackageLayout.FileName)))!["runtimeIdentifier"]!.GetValue<string>(), package = "Sample",
            nativeEntryPoint = File.Exists(Path.Combine(Runtime, "lib/player")) ? "lib/player" : null, files }, JsonOptions));
    }

    private async Task<string> LinuxRuntime(string? extra = null, TarEntryType type = TarEntryType.RegularFile, UnixFileMode? entryMode = null, byte machine = 62)
    {
        File.Delete(Path.Combine(Runtime, "Player.exe"));
        File.WriteAllText(Path.Combine(Runtime, "Player"), "#!/bin/sh\nexec ./lib/player\n");
        var elf = new byte[64];
        new byte[] { 0x7f, 69, 76, 70, 2, 1 }.CopyTo(elf, 0); elf[18] = machine;
        File.WriteAllBytes(Path.Combine(Runtime, "lib/player"), elf);
        var layout = ApplicationPackageLayout.Read(Runtime) with { RuntimeIdentifier = "linux-x64", EntryPoint = "Player" };
        File.WriteAllText(Path.Combine(Runtime, ApplicationPackageLayout.FileName), JsonSerializer.Serialize(layout, JsonOptions));
        await WriteRuntimeManifest();
        var path = Path.Combine(_root, "runtime.tar.gz");
        await using var output = File.Create(path);
        await using var gzip = new GZipStream(output, CompressionLevel.Fastest);
        await using var writer = new TarWriter(gzip);
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.Directory, "runtime with spaces") { Mode = (UnixFileMode)493 });
        foreach (var file in Directory.GetFiles(Runtime, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(Runtime, file).Replace('\\', '/');
            await using var input = File.OpenRead(file);
            await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, "runtime with spaces/" + relative)
            { DataStream = input, Mode = relative is "Player" or "lib/player" ? entryMode ?? (UnixFileMode)493 : (UnixFileMode)420 });
        }
        if (extra is not null)
        {
            var entry = new PaxTarEntry(type, extra) { Mode = (UnixFileMode)420 };
            if (type is TarEntryType.SymbolicLink or TarEntryType.HardLink) entry.LinkName = "../../outside";
            await writer.WriteEntryAsync(entry);
        }
        return path;
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LinuxTarPreservesExecutableModesAndEmptyDirectories(bool galpak)
    {
        var runtime = await LinuxRuntime();
        var output = Path.Combine(_root, "game with spaces.tar.gz");
        var result = await GameDistributionComposer.ComposeAsync(runtime, galpak ? Path.Combine(_root, "archives/Compose.galpak") : Content, output);
        Assert.That(result.Sha256, Is.EqualTo(CryptoHelper.HashSHA256(File.ReadAllBytes(output))));
        using var gzip = new GZipStream(File.OpenRead(output), CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        var modes = new Dictionary<string, UnixFileMode>();
        JsonObject? manifest = null;
        while (reader.GetNextEntry() is { } entry)
        {
            modes.Add(entry.Name.TrimEnd('/'), entry.Mode);
            if (entry.Name.EndsWith("/docs/release-manifest.json")) manifest = JsonNode.Parse(entry.DataStream!)!.AsObject();
        }
        Assert.That(modes["game with spaces/Player"], Is.EqualTo((UnixFileMode)493));
        Assert.That(modes["game with spaces/lib/player"], Is.EqualTo((UnixFileMode)493));
        Assert.That(modes.ContainsKey("game with spaces/pak/ResourcePatches"), Is.True);
        Assert.That(modes.ContainsKey("game with spaces/pak/game.pak"), Is.True);
        Assert.That(manifest!["package"]!.GetValue<string>(), Is.EqualTo("Sample-Game"));
        Assert.That(manifest["content"]!["projectId"]!.GetValue<string>(), Is.EqualTo("compose-project"));
        Assert.That(File.Exists(Path.Combine(Runtime, "pak/game.pak")), Is.False);
    }

    [TestCase("../escape", TarEntryType.RegularFile)]
    [TestCase("runtime with spaces/Player", TarEntryType.RegularFile)]
    [TestCase("runtime with spaces/link", TarEntryType.SymbolicLink)]
    [TestCase("runtime with spaces/hard", TarEntryType.HardLink)]
    [TestCase("runtime with spaces/pipe", TarEntryType.Fifo)]
    public async Task LinuxTarRejectsUnsafeEntries(string extra, TarEntryType type)
    {
        var runtime = await LinuxRuntime(extra, type);
        var output = Path.Combine(_root, "game.tar.gz");
        Assert.ThrowsAsync<InvalidDataException>(() => GameDistributionComposer.ComposeAsync(runtime, Content, output));
        Assert.That(File.Exists(output), Is.False);
        Assert.That(File.Exists(Path.Combine(_root, "escape")), Is.False);
        Assert.That(Directory.GetDirectories(_root, ".compose-*"), Is.Empty);
    }

    [TestCase(420)]
    [TestCase(2541)]
    public async Task LinuxTarRejectsMissingExecuteOrPrivilegedMode(int mode)
    {
        var runtime = await LinuxRuntime(entryMode: (UnixFileMode)mode);
        Assert.ThrowsAsync<InvalidDataException>(() => GameDistributionComposer.ComposeAsync(runtime, Content, Path.Combine(_root, "game.tar.gz")));
    }

    [Test]
    public async Task LinuxTarRejectsWrongArchitectureAndOutputFormat()
    {
        var runtime = await LinuxRuntime();
        Assert.ThrowsAsync<InvalidDataException>(() => GameDistributionComposer.ComposeAsync(runtime, Content, Output));
        runtime = await LinuxRuntime(machine: 183);
        Assert.ThrowsAsync<InvalidDataException>(() => GameDistributionComposer.ComposeAsync(runtime, Content, Path.Combine(_root, "game.tar.gz")));
        // Directory input on Unix reads actual modes; Windows must require the archive.
        if (OperatingSystem.IsWindows())
            Assert.ThrowsAsync<InvalidDataException>(() => GameDistributionComposer.ComposeAsync(Runtime, Content, Path.Combine(_root, "game.tar.gz")));
    }

    private sealed class BuildRunner(Func<IReadOnlyList<string>, CancellationToken, Task<BuildProcessResult>> publish) : IBuildComposeProcessRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public bool MissingSdk { get; init; }
        public int RestoreExitCode { get; init; }
        public async Task<BuildProcessResult> RunAsync(string directory, IReadOnlyList<string> arguments, string stdout, string stderr, CancellationToken ct)
        {
            Calls.Add(arguments);
            await File.WriteAllTextAsync(stdout, "stdout", ct); await File.WriteAllTextAsync(stderr, "stderr", ct);
            if (MissingSdk) throw new System.ComponentModel.Win32Exception(2, "SDK executable was not found");
            if (arguments[0] == "restore") return new(RestoreExitCode, "", "restore");
            return arguments[0] == "--version" ? new(0, "10.0.401\n", "") : await publish(arguments, ct);
        }
    }

    private BuildComposeRequest BuildRequest()
    {
        var project = Path.Combine(_root, "app project", "App.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(project)!); File.WriteAllText(project, "<Project />");
        return new(project, Content, Output, "win-x64", "Player.exe", "1.0.0", SdkVersion: "10.0.401");
    }

    private void FakePublish(IReadOnlyList<string> args, bool corruptContract = false)
    {
        var output = args[args.ToList().IndexOf("-o") + 1];
        Directory.CreateDirectory(output);
        foreach (var file in Directory.GetFiles(Path.Combine(Runtime, "lib"))) File.Copy(file, Path.Combine(output, Path.GetFileName(file)));
        File.Copy(Path.Combine(Runtime, "Player.exe"), Path.Combine(output, "Player.exe"));
        File.WriteAllText(Path.Combine(output, "coreclr.dll"), "fixture, not executed");
        if (corruptContract) File.AppendAllText(Path.Combine(output, "GalNet.Project.Contracts.dll"), "mismatch");
    }

    [Test]
    public async Task SourceBuildPublishesThenReusesStrictCompositionAndEscapesNativeProperty()
    {
        var native = Path.Combine(_root, "native;$().dll"); File.WriteAllText(native, "fixture");
        var request = BuildRequest() with { MiniaudioNativePath = native };
        var runner = new BuildRunner((args, _) => { FakePublish(args); return Task.FromResult(new BuildProcessResult(0, "", "")); });
        var result = await new BuildComposeService(runner).BuildAsync(request);
        Assert.That(result.Success, Is.True, JsonSerializer.Serialize(result));
        var report = (BuildComposeReport)result.Data!;
        Assert.That(report.SdkVersion, Is.EqualTo("10.0.401"));
        Assert.That(report.Steps.Select(step => step.ExitCode), Is.EqualTo(new[] { 0, 0, 0 }));
        Assert.That(File.Exists(Output), Is.True);
        Assert.That(runner.Calls[2], Does.Contain("--no-restore"));
        Assert.That(runner.Calls[1], Does.Contain("-p:RuntimeIdentifier=win-x64"));
        Assert.That(runner.Calls[2].Single(arg => arg.StartsWith("-p:GalNetMiniaudioNativePath=")), Does.Contain("%3B%24%28%29"));
        using var zip = ZipFile.OpenRead(Output);
        Assert.That(zip.GetEntry("lib/Player.exe"), Is.Not.Null);
        Assert.That(zip.GetEntry("pak/game.pak"), Is.Not.Null);
        Assert.That(Directory.GetDirectories(_root, ".build-compose-*"), Is.Empty);
    }

    [TestCase("sdk")]
    [TestCase("publish")]
    [TestCase("restore")]
    [TestCase("contract")]
    public async Task SourceBuildPreservesLogsExitCodesAndInputsOnFailure(string failure)
    {
        var request = BuildRequest();
        if (failure == "sdk") request = request with { SdkVersion = "10.0.999" };
        var before = File.ReadAllBytes(Path.Combine(Content, "game.pak"));
        var runner = new BuildRunner((args, _) =>
        {
            FakePublish(args, failure == "contract");
            return Task.FromResult(new BuildProcessResult(failure == "publish" ? 23 : 0, "", "failure"));
        }) { RestoreExitCode = failure == "restore" ? 17 : 0 };
        var result = await new BuildComposeService(runner).BuildAsync(request);
        Assert.That(result.Success, Is.False);
        var report = (BuildComposeReport)result.Data!;
        Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(failure switch { "sdk" => "build.sdkMismatch", "publish" => "build.publishFailed", "restore" => "build.restoreFailed", _ => "build.failed" }));
        Assert.That(report.Steps.Last().ExitCode, Is.EqualTo(failure == "publish" ? 23 : failure == "restore" ? 17 : 0));
        Assert.That(report.Steps.All(step => File.Exists(step.StandardOutputLog) && File.Exists(step.StandardErrorLog)), Is.True);
        Assert.That(File.Exists(Output), Is.False);
        Assert.That(File.ReadAllBytes(Path.Combine(Content, "game.pak")), Is.EqualTo(before));
        Assert.That(Directory.GetDirectories(_root, ".build-compose-*"), Is.Empty);
    }

    [Test]
    public async Task SourceBuildRejectsConflictsUnsupportedRidAndPropertyInjectionBeforeSdk()
    {
        var request = BuildRequest();
        var runner = new BuildRunner((_, _) => throw new AssertionException("Must not publish"));
        var service = new BuildComposeService(runner);
        Assert.ThrowsAsync<ArgumentException>(() => service.BuildAsync(request with { RuntimeIdentifier = "osx-arm64" }));
        Assert.ThrowsAsync<ArgumentException>(() => service.BuildAsync(request with { Version = "1.0.0;Injected=true" }));
        Assert.ThrowsAsync<InvalidDataException>(() => service.BuildAsync(request with { Output = Path.Combine(Content, "game.zip") }));
        Directory.CreateDirectory(Path.GetDirectoryName(Output)!); File.WriteAllText(Output, "keep");
        Assert.ThrowsAsync<IOException>(() => service.BuildAsync(request));
        Assert.That(runner.Calls, Is.Empty);
        Assert.That(await File.ReadAllTextAsync(Output), Is.EqualTo("keep"));
    }

    [Test]
    public async Task SourceBuildCancellationRemovesOnlyOwnedStage()
    {
        var request = BuildRequest();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new BuildRunner(async (args, ct) =>
        {
            FakePublish(args); entered.SetResult(); await Task.Delay(Timeout.Infinite, ct); return new(0, "", "");
        });
        using var cancellation = new CancellationTokenSource();
        var operation = new BuildComposeService(runner).BuildAsync(request, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        var error = Assert.CatchAsync<OperationCanceledException>(async () => await operation);
        Assert.That(error!.Data["buildReport"], Is.TypeOf<BuildComposeReport>());
        Assert.That(File.Exists(Output), Is.False);
        Assert.That(Directory.GetDirectories(_root, ".build-compose-*"), Is.Empty);
        Assert.That(File.Exists(Path.Combine(Content, "game.pak")), Is.True);
    }

    [Test]
    public async Task SourceBuildReportsMissingSdkWithoutPublishing()
    {
        var runner = new BuildRunner((_, _) => throw new AssertionException("Must not publish")) { MissingSdk = true };
        var result = await new BuildComposeService(runner).BuildAsync(BuildRequest());
        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics.Single().Code, Is.EqualTo("build.sdkUnavailable"));
        Assert.That(((BuildComposeReport)result.Data!).Steps.Single().ExitCode, Is.Null);
        Assert.That(File.Exists(Output), Is.False);
    }

    [Test]
    public async Task LinuxPublishDoesNotTreatManagedSocketsDllAsASharedObjectOnWindows()
    {
        await LinuxRuntime();
        var published = Path.Combine(_root, "published"); Directory.CreateDirectory(published);
        foreach (var file in Directory.GetFiles(Path.Combine(Runtime, "lib"), "*.dll")) File.Copy(file, Path.Combine(published, Path.GetFileName(file)));
        File.Copy(Path.Combine(Runtime, "lib/player"), Path.Combine(published, "Player"));
        File.Copy(Path.Combine(Runtime, "lib/player"), Path.Combine(published, "libcoreclr.so"));
        File.WriteAllText(Path.Combine(published, "System.Net.Sockets.dll"), "Managed PE, not ELF");
        Directory.CreateDirectory(Path.Combine(published, "ThirdParty/SDL3"));
        File.WriteAllText(Path.Combine(published, "ThirdParty/SDL3/SDL-LICENSE.txt"), "fixture SDL license");
        var media = Path.Combine(_root, "media");
        foreach (var name in new[] { "libvlc.so.5", "libvlccore.so.9", "vlc/plugins/codec/libavcodec_plugin.so" })
        {
            var target = Path.Combine(media, "native", name); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(Runtime, "lib/player"), target);
        }
        Directory.CreateDirectory(Path.Combine(media, "licenses"));
        File.WriteAllText(Path.Combine(media, "licenses/fixture.txt"), "fixture");
        File.WriteAllText(Path.Combine(media, "media-manifest.json"), "{}");
        var runtime = Path.Combine(_root, "published-runtime.tar.gz");
        await GameDistributionComposer.PreparePublishedRuntimeAsync(published, runtime, new("linux-x64", "Player", "1.0.0", MediaDirectory: media));
        var output = Path.Combine(_root, "game.tar.gz");
        await GameDistributionComposer.ComposeAsync(runtime, Content, output);
        Assert.That(File.Exists(output), Is.True);
        using (var stream = File.OpenRead(output))
        using (var gzip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress))
        using (var reader = new System.Formats.Tar.TarReader(gzip))
        {
            var entries = new List<string>();
            while (reader.GetNextEntry() is { } entry) entries.Add(entry.Name);
            Assert.That(entries.Any(name => name.EndsWith("/docs/ThirdParty/SDL3/SDL-LICENSE.txt", StringComparison.Ordinal)), Is.True);
            Assert.That(entries.Any(name => name.Contains("/lib/ThirdParty/", StringComparison.Ordinal)), Is.False);
        }
        Assert.That(Directory.GetDirectories(_root, ".prepare-runtime-*"), Is.Empty);
    }
}
