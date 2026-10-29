using GalNet.Media.LibVlc;

namespace GalNet.Presentation.Tests;

public sealed class LibVlcRuntimeTests
{
    [Test]
    public void Platform_factory_failure_is_reported_without_attempting_default_native_initialization()
    {
        var calls = 0;
        using var runtime = new LibVlcRuntime(() => { calls++; throw new PlatformNotSupportedException("Platform video initialization failed."); });
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(runtime.Instance, Is.Null);
        Assert.That(runtime.InitializationError, Is.EqualTo("Platform video initialization failed."));
    }

    [Test]
    public void A_null_platform_factory_result_is_reported_as_an_initialization_failure()
    {
        using var runtime = new LibVlcRuntime(() => null!);
        Assert.That(runtime.Instance, Is.Null);
        Assert.That(runtime.InitializationError, Does.Contain("returned null"));
    }
}
