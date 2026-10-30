using System.Security.Cryptography;
using System.Text;
using GalNet.Storage.FileSystem;

namespace GeneralTest.Storage;

public sealed class GameProfilePathsTests
{
    private string _root = null!;

    [SetUp]
    public void Setup() => _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "galnet-profile-paths-" + Guid.NewGuid().ToString("N"))).FullName;

    [TearDown]
    public void Cleanup() => Directory.Delete(_root, true);

    [Test]
    public void ProjectIdentityKeepsTheExistingDirectoryKeyAcrossContentLocations()
    {
        var profiles = Path.Combine(_root, "profiles");
        foreach (var location in new[] { "old-content", "new-content" })
        {
            var content = Directory.CreateDirectory(Path.Combine(_root, location)).FullName;
            File.WriteAllText(Path.Combine(content, "content.json"), """{"projectId":"same-project"}""");
            var expectedKey = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("project:same-project")));
            Assert.That(GameProfilePaths.GetProfileDirectory(content, profiles), Is.EqualTo(Path.Combine(profiles, expectedKey)));
        }
        Assert.That(Directory.Exists(profiles), Is.False, "Resolving a profile must not create or alter player data.");
    }

    [Test]
    public void InvalidDescriptorKeepsDirectoryFallbackAndCallerOwnsTheRoot()
    {
        var content = Directory.CreateDirectory(Path.Combine(_root, "content")).FullName;
        File.WriteAllText(Path.Combine(content, "content.json"), "broken");
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("directory:" + content)));
        Assert.That(GameProfilePaths.GetProfileDirectory(content, _root), Is.EqualTo(Path.Combine(_root, key)));
        Assert.Throws<ArgumentException>(() => GameProfilePaths.GetProfileDirectory(content, "relative"));
        Assert.Throws<ArgumentException>(() => GameProfilePaths.GetProfileDirectory(content, ""));
    }
}
