using GalNet.Game.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace GeneralTest.Presentation;

public sealed class TypewriterTextBlockTests
{
    [Test]
    public async Task SourceText_UsesTheSharedPortableMarkupParser()
    {
        var control = new TypewriterTextBlock { CharactersPerSecond = 0 };
        control.SourceText = "A\\d{5}B\\nC";

        await control.Completion;

        Assert.That(RenderedText(control), Is.EqualTo("AB\nC"));
        Assert.That(control.IsCompleted, Is.True);
        Assert.That(control.IsRunning, Is.False);
    }

    [Test]
    public async Task SourceText_Renders_Rich_Dialogue_Markup()
    {
        var control = new TypewriterTextBlock { CharactersPerSecond = 0, SourceText = "<b>Bold</b> <i>italic</i> <color=#7DD3FC>blue</color>" };

        await control.Completion;
        var runs = control.Inlines!.OfType<Run>().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(RenderedText(control), Is.EqualTo("Bold italic blue"));
            Assert.That(runs[0].FontWeight, Is.EqualTo(FontWeight.Bold));
            Assert.That(runs[2].FontStyle, Is.EqualTo(FontStyle.Italic));
            Assert.That(runs[4].Foreground, Is.TypeOf<SolidColorBrush>());
        });
    }

    private static string RenderedText(TypewriterTextBlock control) => string.Concat(control.Inlines!.Select(inline => inline switch
    {
        Run run => run.Text,
        LineBreak => "\n",
        _ => string.Empty
    }));
}
