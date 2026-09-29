using System.Text.Json;
using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Core.Gallery;
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
    public async Task BuildAsync_CompilesNestedRawGroupsIntoRuntimeLayout()
    {
        await WriteProjectAsync("story/opening.rawgalgroup", "opening");
        var output = Path.Combine(_root, "Output", "Development");

        var result = await new ProjectContentBuilder().BuildAsync(_root, output);

        Assert.That(result.CompiledGroupCount, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(output, "Graph", "groups", "opening.galgroup")), Is.True);
        Assert.That(Directory.EnumerateFiles(output, "*.rawgalgroup", SearchOption.AllDirectories), Is.Empty);

        var resourceTypes = BuiltinResourceTypes.CreateCatalog();
        var galleryTypes = BuiltinGalleryTypes.CreateCatalog(resourceTypes);
        var content = await new ProjectGameContentProvider(output, resourceTypes, galleryTypes).LoadAsync();
        var entry = (PrimitiveEntry)((Group)content.Graph.Nodes.Single()).Entries.Single();
        Assert.That(entry.Type, Is.EqualTo("dialogue.text"));
        Assert.That(entry.Arguments.GetProperty("content").GetString(), Is.EqualTo("Built from Raw."));
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

        var exception = Assert.ThrowsAsync<InvalidDataException>(() => new ProjectContentBuilder().BuildAsync(_root, output));

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
            edges = Array.Empty<object>()
        };
        await File.WriteAllTextAsync(Path.Combine(graphDirectory, "graph.json"), JsonSerializer.Serialize(graph));
        await WriteRawGroupAsync(rawPath);
    }

    private static Task WriteRawGroupAsync(string path) => File.WriteAllTextAsync(path, """
        {
          "version": 2,
          "kind": "Raw",
          "entries": [
            { "id": "line", "type": "dialogue.text", "parameters": { "speaker": "Guide", "content": "Built from Raw.", "voice": "" } }
          ]
        }
        """);
}
