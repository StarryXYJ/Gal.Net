using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Assets;
using GalNet.Core.Gallery;
using GalNet.Storage.FileSystem;

namespace GeneralTest.Runtime;

public class GameTestCaseTests
{
    [Test]
    public async Task SharedSmokeTestLoadsAsCurrentCompiledContent()
    {
        var directory = FindRepositoryDirectory();

        var resourceTypes = BuiltinResourceTypes.CreateCatalog();
        var galleryTypes = BuiltinGalleryTypes.CreateCatalog(resourceTypes);
        var content = await new ProjectGameContentProvider(
            Path.Combine(directory, "GameTestCase"), resourceTypes, galleryTypes).LoadAsync();

        var groups = content.Graph.Nodes.OfType<Group>().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(content.Graph.RootNodeId, Is.EqualTo("intro"));
            Assert.That(groups, Has.Length.EqualTo(4));
            Assert.That(groups, Has.All.Matches<Group>(group => group.Entries.Count > 0));
            Assert.That(groups.SelectMany(group => group.Entries), Has.All.TypeOf<PrimitiveEntry>());
        });
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
