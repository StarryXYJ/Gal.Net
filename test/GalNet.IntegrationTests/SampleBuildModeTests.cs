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

    [Test]
    public void OrdinaryContentAndProfileArgumentsRemainAvailable()
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
}
