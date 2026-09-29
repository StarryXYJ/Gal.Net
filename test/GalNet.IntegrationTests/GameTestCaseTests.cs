using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Core.Gallery;
using GalNet.Core.Graph;
using GalNet.Editor.Shared.Services;
using GalNet.Storage.FileSystem;

namespace GeneralTest.Runtime;

public class GameTestCaseTests
{
    [Test]
    public async Task SharedSmokeTestBuildsRawContentBeforeLoadingCompiledRuntimeContent()
    {
        var directory = FindRepositoryDirectory();
        var output = Path.Combine(Path.GetTempPath(), $"galnet-game-testcase-{Guid.NewGuid():N}");
        try
        {
            await new ProjectContentBuilder().BuildAsync(Path.Combine(directory, "GameTestCase"), output);

            var resourceTypes = BuiltinResourceTypes.CreateCatalog();
            var galleryTypes = BuiltinGalleryTypes.CreateCatalog(resourceTypes);
            var content = await new ProjectGameContentProvider(output, resourceTypes, galleryTypes).LoadAsync();

            var groups = content.Graph.Nodes.OfType<Group>().ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(content.Graph.RootNodeId, Is.EqualTo("intro"));
                Assert.That(groups, Has.Length.EqualTo(4));
                Assert.That(groups, Has.All.Matches<Group>(group => group.Entries.Count > 0));
                Assert.That(groups.SelectMany(group => group.Entries), Has.All.TypeOf<PrimitiveEntry>());
                Assert.That(groups.Single(group => group.Id == "intro").Entries.OfType<PrimitiveEntry>(),
                    Has.One.Matches<PrimitiveEntry>(entry =>
                        entry.Type == "gallery.unlock" && entry.Arguments.GetProperty("id").GetInt32() == 100));
                Assert.That(content.Gallery.GetItems("cg"), Has.One.Matches<GalNet.Core.Gallery.GalleryItem>(item =>
                    item.Id == 100 && item.ResourceId == "a1000000000000000000000000000001"));
            });
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }

    private static string FindRepositoryDirectory()
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "GameTestCase")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository GameTestCase directory.");
    }
}
