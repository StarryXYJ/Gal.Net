using GalNet.Game.Controls;

namespace GeneralTest.Presentation;

public sealed class TypewriterTextBlockTests
{
    [Test]
    public async Task SourceText_UsesTheSharedPortableMarkupParser()
    {
        var control = new TypewriterTextBlock { CharactersPerSecond = 0 };
        control.SourceText = "A\\d{5}B\\nC";

        await control.Completion;

        Assert.That(control.Text, Is.EqualTo("AB\nC"));
        Assert.That(control.IsCompleted, Is.True);
        Assert.That(control.IsRunning, Is.False);
    }
}
