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

    [TestCase("Bound", false)]
    [TestCase("BoundDeveloper", true)]
    public void BoundModeRejectsOtherProjectsButAcceptsUpdatedBuildsOfItsOwnProject(string mode, bool debugging)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "galnet-binding-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var policy = new GameLaunchPolicy(mode, "custom-game");
            Assert.That(policy.AllowsExternalContent, Is.False);
            Assert.That(policy.AllowsDebugging, Is.EqualTo(debugging));
            foreach (var build in new[] { 'a', 'b' })
            {
                InstalledGameFixture.Write(root, "custom-game", build);
                Assert.DoesNotThrow(() => policy.ValidateContent(root));
            }
            InstalledGameFixture.Write(root, "another-game");
            Assert.Throws<InvalidDataException>(() => policy.ValidateContent(root));
            Assert.Throws<InvalidDataException>(() => policy.ValidateContent(null));
            Assert.Throws<InvalidDataException>(() => new GameLaunchPolicy("Bound").ValidateContent(root));
            Assert.Throws<InvalidDataException>(() => new GameLaunchPolicy("invalid").ValidateContent(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
