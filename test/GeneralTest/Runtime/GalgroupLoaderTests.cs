using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Runtime.Loader;

namespace GeneralTest.Runtime;

public class GalgroupLoaderTests
{
    [Test]
    public void LoadIntoGroup_Should_Create_Concrete_Entries()
    {
        var group = new Group { Id = "test_group" };
        GalgroupLoader.LoadIntoGroupFromContent(group, """
            text : speaker:Alice; content:intro_line1
            audio.play : channel:bgm; asset:bgm_01; mode:loop
            layer.show : id:bg; asset:bg_classroom
            """);

        Assert.That(group.Entries[0], Is.TypeOf<TextEntry>());
        Assert.That(group.Entries[1], Is.TypeOf<PlayAudioEntry>());
        Assert.That(group.Entries[2], Is.TypeOf<ShowLayerEntry>());
    }

    [Test]
    public void Load_Should_Parse_Escapes_And_Clean_Unknown_Parameters()
    {
        var group = new Group { Id = "test" };
        GalgroupLoader.LoadIntoGroupFromContent(group, @"text : content:greeting\:key; obsolete:value");
        Assert.That(group.Entries[0].Values["content"], Is.EqualTo("greeting:key"));
        Assert.That(group.Entries[0].Values, Does.Not.ContainKey("obsolete"));
    }

    [Test]
    public void Load_Unknown_Type_Should_Throw()
    {
        var group = new Group { Id = "test" };
        Assert.That(() => GalgroupLoader.LoadIntoGroupFromContent(group, "jump : target:end"), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Entries_Should_Keep_Sequential_Ids()
    {
        var group = new Group { Id = "test" };
        GalgroupLoader.LoadIntoGroupFromContent(group, "text : content:a\nwait : duration:0.5\ntext : content:b");
        Assert.That(group.Entries.Select(x => x.Id), Is.EqualTo(new[] { 1, 2, 3 }));
    }
}
