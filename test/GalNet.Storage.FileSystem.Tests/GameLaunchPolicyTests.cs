using GalNet.Storage.FileSystem;

namespace GeneralTest.Storage;

public sealed class GameLaunchPolicyTests
{
    [TestCase("Developer", true)]
    [TestCase("Player", false)]
    public void ExternalModesAllowDifferentProjectsAndDoNotRequireInstalledContent(string mode, bool debugging)
    {
        var policy = new GameLaunchPolicy(mode);
        Assert.Multiple(() =>
        {
            Assert.That(policy.AllowsExternalContent, Is.True);
            Assert.That(policy.AllowsDebugging, Is.EqualTo(debugging));
            Assert.DoesNotThrow(() => policy.ValidateContent(null));
            Assert.DoesNotThrow(() => policy.ValidateContent("not-installed"));
        });
    }

    [Test]
    public void BoundModeRejectsOtherProjectsButAcceptsUpdatedBuildsOfItsOwnProject()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "galnet-binding-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var policy = new GameLaunchPolicy("Bound", "custom-game");
            Assert.That(policy.AllowsExternalContent, Is.False);
            Assert.That(policy.AllowsDebugging, Is.False);
            foreach (var build in new[] { "first-build", "updated-build" })
            {
                File.WriteAllText(Path.Combine(root, "content.json"), $$"""{"projectId":"custom-game","buildId":"{{build}}"}""");
                Assert.DoesNotThrow(() => policy.ValidateContent(root));
            }
            File.WriteAllText(Path.Combine(root, "content.json"), """{"projectId":"another-game"}""");
            Assert.Throws<InvalidDataException>(() => policy.ValidateContent(root));
            Assert.Throws<InvalidDataException>(() => policy.ValidateContent(null));
            Assert.Throws<InvalidDataException>(() => new GameLaunchPolicy("Bound").ValidateContent(root));
            Assert.Throws<InvalidDataException>(() => new GameLaunchPolicy("invalid").ValidateContent(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
