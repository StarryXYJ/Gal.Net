using GalNet.Core.Graph;
using GalNet.Core.Handler;
using GalNet.Runtime.Handlers;

namespace GeneralTest.Runtime.Handlers;

public class EntryHandlerRegistryTests
{
    [Test]
    public void CreateDefault_Should_Register_All_Handlers()
    {
        var registry = EntryHandlerRegistry.CreateDefault();

        Assert.That(registry.Resolve("text"), Is.Not.Null);
        Assert.That(registry.Resolve("audio"), Is.Not.Null);
        Assert.That(registry.Resolve("layer"), Is.Not.Null);
        Assert.That(registry.Resolve("effect"), Is.Not.Null);
        Assert.That(registry.Resolve("control"), Is.Not.Null);
        Assert.That(registry.Resolve("wait"), Is.Not.Null);
        Assert.That(registry.Resolve("variable"), Is.Not.Null);
        Assert.That(registry.Resolve("jump"), Is.Not.Null);
        Assert.That(registry.Resolve("video"), Is.Not.Null);
    }

    [Test]
    public void Resolve_Unknown_Should_Return_Null()
    {
        var registry = EntryHandlerRegistry.CreateDefault();
        Assert.That(registry.Resolve("nonexistent"), Is.Null);
    }

    [Test]
    public void Handler_Properties_Should_Match()
    {
        var registry = EntryHandlerRegistry.CreateDefault();

        var text = registry.Resolve("text")!;
        Assert.That(text.EntryType, Is.EqualTo("text"));
        Assert.That(text.IsBlocking, Is.True);

        var audio = registry.Resolve("audio")!;
        Assert.That(audio.EntryType, Is.EqualTo("audio"));
        Assert.That(audio.IsBlocking, Is.False);
    }
}
