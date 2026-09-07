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
    public void Checkpoint_Metadata_Should_Only_Mark_Input_Boundaries()
    {
        var registry = EntryHandlerRegistry.CreateDefault();
        Assert.That(registry.Resolve(TextEntry.TypeId)!.CreatesCheckpoint, Is.True);
        Assert.That(registry.Resolve(WaitEntry.TypeId)!.CreatesCheckpoint, Is.False);
        Assert.That(registry.Resolve(PlayAudioEntry.TypeId)!.CreatesCheckpoint, Is.False);
    }
}
