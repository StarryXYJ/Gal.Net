using GalNet.Core.Text;

namespace GeneralTest.Runtime;

public sealed class TypewriterTextParserTests
{
    [Test]
    public void Parse_Recognizes_Delay_Instant_And_Newline()
    {
        var tokens = TypewriterTextParser.Parse("A\\d{120}B\\d-C\\nD");

        Assert.That(tokens.Select(token => token.Kind), Is.EqualTo(new[]
        {
            TypewriterTokenKind.Text, TypewriterTokenKind.Delay, TypewriterTokenKind.Text,
            TypewriterTokenKind.Instant, TypewriterTokenKind.Text
        }));
        Assert.That(tokens[1].DelayMilliseconds, Is.EqualTo(120));
        Assert.That(tokens[^1].Text, Is.EqualTo("C\nD"));
    }

    [Test]
    public void Rich_parse_preserves_styles_and_typewriter_directives()
    {
        var tokens = RichTypewriterTextParser.Parse("<b>Bold</b> <i>italic</i> <color=#7DD3FC>blue</color>\\d{120}done<br>next");

        Assert.Multiple(() =>
        {
            Assert.That(tokens.Select(token => token.Kind), Is.EqualTo(new[]
            {
                RichTypewriterTokenKind.Text, RichTypewriterTokenKind.Text, RichTypewriterTokenKind.Text,
                RichTypewriterTokenKind.Text, RichTypewriterTokenKind.Text, RichTypewriterTokenKind.Delay,
                RichTypewriterTokenKind.Text, RichTypewriterTokenKind.LineBreak, RichTypewriterTokenKind.Text
            }));
            Assert.That(tokens[0], Is.EqualTo(new RichTypewriterToken(RichTypewriterTokenKind.Text, "Bold", IsBold: true)));
            Assert.That(tokens[2], Is.EqualTo(new RichTypewriterToken(RichTypewriterTokenKind.Text, "italic", IsItalic: true)));
            Assert.That(tokens[5].DelayMilliseconds, Is.EqualTo(120));
            Assert.That(tokens[4].Color, Is.EqualTo("#7DD3FC"));
        });
    }
}
