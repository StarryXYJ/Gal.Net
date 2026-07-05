using GalNet.Core.Graph;
using GalNet.Runtime.Loader;

namespace GeneralTest.Runtime.Loader;

public class GalgroupLoaderTests
{
    [Test]
    public void LoadIntoGroup_Should_Use_English_Types_Directly()
    {
        var group = new Group { Id = "test_group" };
        var content = """
            text : speaker:Alice; content:intro_line1
            audio : channel:bgm; asset:bgm_01; mode:repeat
            layer : action:show; id:bg; asset:bg_classroom
            """;

        GalgroupLoader.LoadIntoGroupFromContent(group, content);

        Assert.That(group.Entries, Has.Count.EqualTo(3));
        Assert.That(group.Entries[0].Type, Is.EqualTo("text"));
        Assert.That(group.Entries[1].Type, Is.EqualTo("audio"));
        Assert.That(group.Entries[2].Type, Is.EqualTo("layer"));
    }

    [Test]
    public void LoadIntoGroup_Should_Parse_Params()
    {
        var group = new Group { Id = "test" };
        var content = "wait : duration:2.5";

        GalgroupLoader.LoadIntoGroupFromContent(group, content);

        Assert.That(group.Entries[0].Type, Is.EqualTo("wait"));
        Assert.That(group.Entries[0].Params["duration"], Is.EqualTo("2.5"));
    }

    [Test]
    public void LoadIntoGroup_Should_Parse_Escaped_Colon()
    {
        var group = new Group { Id = "test" };
        var content = @"text : speaker:Alice; content:greeting\:colon_in_key";

        GalgroupLoader.LoadIntoGroupFromContent(group, content);

        Assert.That(group.Entries[0].Params["content"], Is.EqualTo("greeting:colon_in_key"));
    }

    [Test]
    public void Compile_Should_Produce_SimpleEntries()
    {
        var group = new Group { Id = "test" };
        var content = """
            text : speaker:Alice; content:line1_key
            wait : duration:0.5
            text : speaker:Bob; content:line2_key
            """;

        GalgroupLoader.LoadIntoGroupFromContent(group, content);

        var compiled = group.Entries.SelectMany(e => e.Compile()).ToList();
        Assert.That(compiled, Has.Count.EqualTo(3));
        Assert.That(compiled[0].Type, Is.EqualTo("text"));
        Assert.That(compiled[0].Id, Is.EqualTo("1"));
        Assert.That(compiled[1].Id, Is.EqualTo("2"));
        Assert.That(compiled[2].Id, Is.EqualTo("3"));
    }
}
