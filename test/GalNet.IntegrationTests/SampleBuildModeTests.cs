namespace GalNet.IntegrationTests;

public sealed class SampleBuildModeTests
{
    [Test]
    public void BuildContainsOnlyTheSelectedDebugCapability()
    {
        var assembly = typeof(GalNet.Sample.CrossPlatform.App).Assembly;
        var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
#if GALNET_GAME_DEBUG
        Assert.That(references, Does.Contain("GalNet.Avalonia.Debugging"));
        Assert.That(typeof(GalNet.Sample.CrossPlatform.App).GetProperty("DebugInvitation"), Is.Not.Null);
#else
        Assert.That(references, Does.Not.Contain("GalNet.Debugging"));
        Assert.That(references, Does.Not.Contain("GalNet.Avalonia.Debugging"));
        Assert.That(typeof(GalNet.Sample.CrossPlatform.App).GetProperty("DebugInvitation"), Is.Null);
#endif
    }

    [Test]
    public void PackagedProfileFollowsProjectIdentityAcrossMovesAndBuilds()
    {
        var root = Path.Combine(Path.GetTempPath(), "galnet-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = Path.Combine(root, "first", "pak");
            var second = Path.Combine(root, "second", "pak");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            File.WriteAllText(Path.Combine(first, "content.json"), """{"projectId":"../../same-project","buildId":"first"}""");
            File.WriteAllText(Path.Combine(second, "content.json"), """{"projectId":"../../same-project","buildId":"second"}""");
            var profile = GalNet.Sample.CrossPlatform.SampleCustomization.GetProfileDirectory(first);
            Assert.That(GalNet.Sample.CrossPlatform.SampleCustomization.GetProfileDirectory(second), Is.EqualTo(profile));
            Assert.That(Path.GetFileName(profile), Does.Match("^[a-f0-9]{64}$"));
            File.WriteAllText(Path.Combine(second, "content.json"), """{"projectId":"another-project"}""");
            Assert.That(GalNet.Sample.CrossPlatform.SampleCustomization.GetProfileDirectory(second), Is.Not.EqualTo(profile));
            File.WriteAllText(Path.Combine(second, "content.json"), "broken JSON");
            Assert.DoesNotThrow(() => GalNet.Sample.CrossPlatform.SampleCustomization.GetProfileDirectory(second));
            Assert.That(Directory.Exists(profile), Is.False);
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void PackagedAssemblyDirectoryResolvesTheRootApphost()
    {
        var root = Path.Combine(Path.GetTempPath(), "galnet-app-root-" + Guid.NewGuid().ToString("N"));
        var library = Path.Combine(root, "lib");
        Directory.CreateDirectory(library);
        try
        {
            File.WriteAllText(Path.Combine(root, "GalNet.Sample.CrossPlatform.Desktop.exe"), "apphost marker");
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            File.WriteAllText(Path.Combine(root, "docs/application-layout.json"), """{"schemaVersion":1,"runtimeIdentifier":"win-x64","entryPoint":"GalNet.Sample.CrossPlatform.Desktop.exe","managedDirectory":"lib","contractsDirectory":"lib","contentDirectory":"pak"}""");
            Assert.That(GalNet.Storage.FileSystem.ApplicationPackageLayout.FindApplicationRoot(library), Is.EqualTo(root));
            Assert.That(GalNet.Storage.FileSystem.ApplicationPackageLayout.FindApplicationRoot(root), Is.EqualTo(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
