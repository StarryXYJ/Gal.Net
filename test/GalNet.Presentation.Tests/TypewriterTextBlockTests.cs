using Avalonia.Controls.Documents;
using Avalonia.Media;
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

    [Test]
    public async Task ReadColorChangesOnlyDefaultRunsWithoutRestartingThePresentation()
    {
        var control = new TypewriterTextBlock { Foreground = Brushes.White, CharactersPerSecond = 0 };
        ReadTextStyle.SetWasRead(control, true);
        ReadTextStyle.SetForeground(control, Brushes.Cyan);
        control.SourceText = "<b>default</b><color=#FF0000><i>explicit</i></color>";
        await control.Completion;
        var completion = control.Completion;
        var runs = control.Inlines!.OfType<Run>().ToArray();
        Assert.That(((ISolidColorBrush)runs[0].Foreground!).Color, Is.EqualTo(Colors.Cyan));
        Assert.That(((ISolidColorBrush)runs[1].Foreground!).Color, Is.EqualTo(Colors.Red));
        ReadTextStyle.SetForeground(control, Brushes.Lime);
        Assert.That(control.Completion, Is.SameAs(completion));
        Assert.That(((ISolidColorBrush)runs[0].Foreground!).Color, Is.EqualTo(Colors.Lime));
        Assert.That(((ISolidColorBrush)runs[1].Foreground!).Color, Is.EqualTo(Colors.Red));
        Assert.That(runs[0].FontWeight, Is.EqualTo(FontWeight.Bold));
        Assert.That(runs[1].FontStyle, Is.EqualTo(FontStyle.Italic));
        ReadTextStyle.SetForeground(control, null);
        Assert.That(((ISolidColorBrush)runs[0].Foreground!).Color, Is.EqualTo(Colors.White));
    }
}
