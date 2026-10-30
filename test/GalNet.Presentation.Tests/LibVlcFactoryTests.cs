using GalNet.Avalonia.GameView.Composition;
using GalNet.Avalonia.Themes;
using GalNet.Media.LibVlc;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Presentation.Tests;

public sealed class LibVlcFactoryTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void Relative_native_or_plugin_paths_are_rejected_before_loading(bool plugin)
    {
        var options = plugin
            ? new LibVlcOptions { UseSystemLibraries = true, PluginDirectory = "relative/plugins" }
            : new LibVlcOptions { NativeLibraryDirectory = "relative/native" };
        Assert.Throws<ArgumentException>(() => new LibVlcFactory(options));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Private_and_system_loading_are_mutually_exclusive(bool both)
    {
        var options = new LibVlcOptions { UseSystemLibraries = both, NativeLibraryDirectory = both ? Path.GetTempPath() : null };
        Assert.Throws<ArgumentException>(() => new LibVlcFactory(options));
    }

    [Test]
    public void Missing_private_deployment_is_reported_without_system_fallback()
    {
        var path = Path.Combine(Path.GetTempPath(), "galnet-vlc-missing-" + Guid.NewGuid().ToString("N"));
        var factory = new LibVlcFactory(new() { NativeLibraryDirectory = path });
        using var runtime = new LibVlcRuntime(factory: factory);
        Assert.That(runtime.Instance, Is.Null);
        Assert.That(runtime.Factory, Is.SameAs(factory));
        Assert.That(runtime.InitializationError, Does.Contain(path));
        Assert.That(factory.InitializationError, Does.Contain("RID="));
        Assert.Throws<InvalidOperationException>(() => factory.Create());
        Assert.That(Directory.Exists(path), Is.False);
    }

    [Test]
    public void Page_registration_preserves_the_hosts_explicit_factory()
    {
        var factory = new LibVlcFactory(new() { UseSystemLibraries = true });
        var services = new ServiceCollection().AddSingleton(factory);
        services.AddAvaloniaGameViewPages(new ClassicTheme());
        using var provider = services.BuildServiceProvider();
        Assert.That(provider.GetRequiredService<LibVlcFactory>(), Is.SameAs(factory));
        Assert.That(factory.NativeVersion, Is.Null, "Registering pages must not initialize native media.");
    }
}
