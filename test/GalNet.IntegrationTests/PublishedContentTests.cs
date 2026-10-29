using GalNet.Core.Assets;
using GalNet.Sample.Avalonia;
using GalNet.Storage.FileSystem;

namespace GalNet.IntegrationTests;

public sealed class PublishedContentTests
{
    [TestCase("SelfContained")]
    [TestCase("FrameworkDependent")]
    [Explicit("Loads the actual extracted release content; set ReleaseExtractionDirectory in runsettings.")]
    public async Task IntegratedReleaseLoadsThePackagedGraphAndResources(string deployment)
    {
        var extraction = TestContext.Parameters.Get("ReleaseExtractionDirectory", "");
        Assert.That(extraction, Is.Not.Empty, "Set ReleaseExtractionDirectory to Test-Release.ps1's extraction directory.");
        var contentRoot = Path.Combine(extraction, deployment, "Sample-GameTestCase", "pak");
        var catalogs = SampleCustomization.CreateContentCatalogs();
        using var resources = await GameResourceScope.OpenAsync(contentRoot, catalogs.Resources, catalogs.Gallery,
            SampleCustomization.CreateHostOptions().Content);
        Assert.That(resources.Content.Graph.ContentBuildId, Does.Match("^[a-f0-9]{64}$"));
        Assert.That(resources.Content.Graph.Nodes, Is.Not.Empty);
        Assert.That((await resources.Assets.GetFileAsync(StoryResourceIds.Graph))?.TypeId, Is.EqualTo("graph"));
        Assert.That(Directory.Exists(Path.Combine(contentRoot, "Graph")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(contentRoot, ".galnet")), Is.False);
    }
}
