using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Editor.Shared.Services;
using GalNet.Storage.FileSystem;

namespace GeneralTest.Editor;

public sealed class ProjectContentBuilderTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp() => _root = Path.Combine(Path.GetTempPath(), $"galnet-build-test-{Guid.NewGuid():N}");

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Test]
    public async Task BuildReturnsTheSourceMapOfItsActualCompiledFiles()
    {
        await WriteProjectAsync("groups/source.rawgalgroup", "opening");
        var output = Path.Combine(_root, "Output", "Debug");
        var result = await new ProjectContentBuilder(new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules())).BuildAsync(_root, output);
        using var compiled = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "Graph", "groups", "opening.galgroup")));
        var ids = compiled.RootElement.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("id").GetString()).ToArray();
        Assert.That(result.SourceEntries.SelectMany(entry => entry.PrimitiveIds), Is.EqualTo(ids));
        Assert.That(result.SourceEntries.First().Start, Is.Zero);
        Assert.That(result.SourceEntries.Last().End, Is.EqualTo(ids.Length));
        var signature = result.AuthorSignature;
        await File.WriteAllTextAsync(Path.Combine(_root, "Graph", "groups", "source.rawgalgroup"), "changed after build");
        Assert.That(result.AuthorSignature, Is.EqualTo(signature));
        Assert.That(result.AuthorSignature, Has.Length.EqualTo(64));
    }

    [Test]
    public async Task BuildAsync_RejectsMissingDevelopmentTextBeforeReplacingOutputEvenWhenExternalHasIt()
    {
        await WriteProjectAsync("groups/opening.rawgalgroup", "opening");
        await File.WriteAllTextAsync(Path.Combine(_root, "I18n/zh-CN/opening.json"), """{"character.guide.name":"向导"}""");
        Directory.CreateDirectory(Path.Combine(_root, "I18n/External/zh-CN"));
        await File.WriteAllTextAsync(Path.Combine(_root, "I18n/External/zh-CN/extra.json"), """{"group.opening.entry.line.content":"external"}""");
        var output = Path.Combine(_root, "Output"); Directory.CreateDirectory(output); await File.WriteAllTextAsync(Path.Combine(output, "sentinel.txt"), "keep");
        var failure = Assert.ThrowsAsync<InvalidDataException>(() => new ProjectContentBuilder(new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules())).BuildAsync(_root, output));
        Assert.That(failure!.Message, Does.Contain("Unresolved reference")); Assert.That(File.ReadAllText(Path.Combine(output, "sentinel.txt")), Is.EqualTo("keep"));
        var issue = LocalizationValidation.ValidateDisk(_root, new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules())).Single(item => item.Code == "localization.text.defaultMissing");
        Assert.That(issue.JsonPointer, Is.EqualTo("/entries/0/parameters/content")); Assert.That(issue.ObjectId, Is.EqualTo("line"));
    }

    [Test]
    public async Task BuildAsync_CompilesNestedRawGroupsIntoRuntimeLayout()
    {
        await WriteProjectAsync("story/opening.rawgalgroup", "opening");
        var output = Path.Combine(_root, "Output", "Development");

        var result = await new ProjectContentBuilder(new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules())).BuildAsync(_root, output);

        Assert.That(result.CompiledGroupCount, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(output, "Graph", "groups", "opening.galgroup")), Is.True);
        Assert.That(Directory.EnumerateFiles(output, "*.rawgalgroup", SearchOption.AllDirectories), Is.Empty);

        var resourceTypes = BuiltinResourceTypes.CreateCatalog();
        var galleryTypes = BuiltinGalleryTypes.CreateCatalog(resourceTypes);
        var content = await new ProjectGameContentProvider(output, resourceTypes, galleryTypes).LoadAsync();
        var entry = (PrimitiveEntry)((Group)content.Graph.Nodes.Single()).Entries.Single();
        Assert.That(entry.Type, Is.EqualTo("dialogue.text"));
        Assert.That(entry.Arguments.GetProperty("content").GetString(), Is.EqualTo("group.opening.entry.line.content"));
    }

    [Test]
    public async Task BuildAsync_RejectsOrphanRawSourceWithoutReplacingPriorOutput()
    {
        await WriteProjectAsync("groups/opening.rawgalgroup", "opening");
        var output = Path.Combine(_root, "Output", "Development");
        Directory.CreateDirectory(output);
        var sentinel = Path.Combine(output, "sentinel.txt");
        await File.WriteAllTextAsync(sentinel, "preserve");
        await WriteRawGroupAsync(Path.Combine(_root, "Graph", "groups", "orphan.rawgalgroup"));

        var exception = Assert.ThrowsAsync<InvalidDataException>(() => new ProjectContentBuilder(new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules())).BuildAsync(_root, output));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("not referenced"));
            Assert.That(File.ReadAllText(sentinel), Is.EqualTo("preserve"));
        });
    }

    private async Task WriteProjectAsync(string groupFile, string groupId)
    {
        var graphDirectory = Path.Combine(_root, "Graph");
        var rawPath = Path.Combine(graphDirectory, groupFile.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(rawPath)!);
        var graph = new
        {
            version = 2,
            name = "Build test",
            rootNodeId = groupId,
            nodes = new[] { new { id = groupId, type = "Group", name = "Opening", file = groupFile } },
            edges = Array.Empty<object>(),
            playerVariables = Array.Empty<object>(),
            saveVariables = Array.Empty<object>()
        };
        await File.WriteAllTextAsync(Path.Combine(graphDirectory, "graph.json"), JsonSerializer.Serialize(graph));
        await WriteRawGroupAsync(rawPath);
        Directory.CreateDirectory(Path.Combine(_root, "I18n", "catalog"));
        Directory.CreateDirectory(Path.Combine(_root, "I18n", "zh-CN"));
        await File.WriteAllTextAsync(Path.Combine(_root, "I18n", "catalog", "characters.json"),
            """[{"id":"guide","displayNameKey":"character.guide.name"}]""");
        await File.WriteAllTextAsync(Path.Combine(_root, "I18n", "zh-CN", "opening.json"),
            """{"character.guide.name":"向导","group.opening.entry.line.content":"由 raw 编译。"}""");
    }

    private static Task WriteRawGroupAsync(string path) => File.WriteAllTextAsync(path, """
        {
          "version": 2,
          "kind": "Raw",
          "entries": [
            { "id": "line", "type": "dialogue.text", "parameters": { "speaker": "guide", "content": "group.opening.entry.line.content" } }
          ]
        }
        """);
}
