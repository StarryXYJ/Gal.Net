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
}
