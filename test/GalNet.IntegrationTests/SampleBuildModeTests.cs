using GalNet.Sample.Avalonia;

namespace GalNet.IntegrationTests;

public sealed class SampleBuildModeTests
{
    [Test]
    public void BuildContainsOnlyTheSelectedDebugCapability()
    {
        var assembly = typeof(GameLaunchOptions).Assembly;
        var debugTypes = assembly.GetTypes().Where(type => type.Namespace == "GalNet.Sample.Avalonia.Debug").ToArray();
        var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
#if GALNET_GAME_DEBUG
        Assert.That(debugTypes, Is.Not.Empty);
        Assert.That(references, Does.Contain("GalNet.Debugging"));
        Assert.That(assembly.GetType("GalNet.Sample.Avalonia.Views.DebugMenuView"), Is.Not.Null);
        Assert.That(typeof(GameLaunchOptions).GetProperty("DebugInvitation"), Is.Not.Null);
#else
        Assert.That(debugTypes, Is.Empty);
        Assert.That(references, Does.Not.Contain("GalNet.Debugging"));
        Assert.That(references, Does.Not.Contain("AvaloniaUI.DiagnosticsSupport"));
        Assert.That(assembly.GetType("GalNet.Sample.Avalonia.Views.DebugMenuView"), Is.Null);
        Assert.That(assembly.GetType("GalNet.Sample.Avalonia.Views.DebugLogWindow"), Is.Null);
        Assert.That(typeof(GameLaunchOptions).GetProperty("DebugInvitation"), Is.Null);
#endif
    }

#if GALNET_LAUNCH_ARGS
    [Test]
    public void DeveloperBuildAcceptsContentAndProfileArguments()
    {
        var options = GameLaunchOptions.Parse(["game", "--profile", "profile"]);
        Assert.That(options.GameDirectory, Is.EqualTo(Path.GetFullPath("game")));
        Assert.That(options.ProfileDirectory, Is.EqualTo(Path.GetFullPath("profile")));
    }

#if !GALNET_GAME_DEBUG
    [Test]
    public void PlayerBuildRejectsDebugLaunchArguments()
    {
        var error = Assert.Throws<ArgumentException>(() => GameLaunchOptions.Parse(["game", "--debug-port", "1234"]));
        Assert.That(error!.Message, Does.Contain("without game debugging support"));
    }
#endif
#else
    [TestCase("game")]
    [TestCase("--profile")]
    [TestCase("--debug-port")]
    public void PlayerBuildRejectsLaunchArguments(string argument)
    {
        var error = Assert.Throws<ArgumentException>(() => GameLaunchOptions.Parse([argument]));
        Assert.That(error!.Message, Does.Contain("without launch argument support"));
    }
#endif

    [Test]
    public void DefaultLaunchUsesApplicationPakAndUserDataInsteadOfWorkingDirectory()
    {
        var application = Path.Combine(Path.GetTempPath(), "galnet-launch-" + Guid.NewGuid().ToString("N"));
        var options = GameLaunchOptions.Parse([], application);
        Assert.That(options.GameDirectory, Is.EqualTo(Path.Combine(application, "pak")));
        Assert.That(options.ProfileDirectory, Does.StartWith(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GalNet") + Path.DirectorySeparatorChar));
        Assert.That(options.LogDirectory, Is.EqualTo(Path.Combine(options.ProfileDirectory!, "logs")));
        Assert.That(Directory.Exists(application), Is.False);
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
            var profile = GameLaunchOptions.GetDefaultProfileDirectory(first);
            Assert.That(GameLaunchOptions.GetDefaultProfileDirectory(second), Is.EqualTo(profile));
            Assert.That(Path.GetFileName(profile), Does.Match("^[a-f0-9]{64}$"));
            File.WriteAllText(Path.Combine(second, "content.json"), """{"projectId":"another-project"}""");
            Assert.That(GameLaunchOptions.GetDefaultProfileDirectory(second), Is.Not.EqualTo(profile));
            File.WriteAllText(Path.Combine(second, "content.json"), "broken JSON");
            Assert.DoesNotThrow(() => GameLaunchOptions.Parse([], Path.GetDirectoryName(second)!));
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
            File.WriteAllText(Path.Combine(root, "GalNet.Sample.Avalonia.exe"), "apphost marker");
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            File.WriteAllText(Path.Combine(root, "docs/application-layout.json"), """{"schemaVersion":1,"runtimeIdentifier":"win-x64","entryPoint":"GalNet.Sample.Avalonia.exe","managedDirectory":"lib","contractsDirectory":"lib","contentDirectory":"pak"}""");
            Assert.That(GameLaunchOptions.GetApplicationDirectory(library), Is.EqualTo(root));
            Assert.That(GameLaunchOptions.GetApplicationDirectory(root), Is.EqualTo(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
