using GalNet.Core.Entry;
using GalNet.Runtime.Handlers;

namespace GeneralTest.Runtime;

public class EntryHandlerRegistryTests
{
    [Test]
    public void CreateDefault_Should_Register_Every_Core_Entry()
    {
        var registry = EntryHandlerRegistry.CreateDefault();
        foreach (var definition in EntryRegistry.Definitions.Where(x => x.Type != UnlockGalleryEntry.TypeId))
            Assert.That(registry.Resolve(definition.Type), Is.Not.Null, definition.Type);
        Assert.That(registry.Resolve("jump"), Is.Null);
    }

    [Test]
    public void Blocking_Metadata_Should_Match()
    {
        var registry = EntryHandlerRegistry.CreateDefault();
        Assert.That(registry.Resolve(TextEntry.TypeId)!.IsBlocking, Is.True);
        Assert.That(registry.Resolve(WaitEntry.TypeId)!.IsBlocking, Is.True);
        Assert.That(registry.Resolve(PlayAudioEntry.TypeId)!.IsBlocking, Is.False);
    }
}
