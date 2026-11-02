using GalNet.Core.Text;

namespace GeneralTest.Runtime;

public sealed class TypewriterTextParserTests
{
    private static readonly RichTypewriterTokenKind[] ExpectedRichTokenKinds =
    [
        RichTypewriterTokenKind.Text, RichTypewriterTokenKind.Text, RichTypewriterTokenKind.Text,
        RichTypewriterTokenKind.Text, RichTypewriterTokenKind.Text, RichTypewriterTokenKind.Delay,
        RichTypewriterTokenKind.Text, RichTypewriterTokenKind.LineBreak, RichTypewriterTokenKind.Text
    ];
    private static readonly string[] ExpectedPlainText = ["A", "<b>B</b>\\skip\\skipper"];

    [Test]
    public void Rich_sizes_compose_relative_to_parent_and_restore_after_closing()
    {
        var tokens = RichTypewriterTextParser.Parse("A<size=+4>B<size=150%>C</size>D</size>E<size=24>F<size=-2>G</size></size>");
        Assert.That(tokens.Select(token => (token.Text, token.FontSizeScale, token.FontSizeOffset)), Is.EqualTo(new[]
        {
            ("A", 1d, 0d), ("B", 1d, 4d), ("C", 1.5d, 6d), ("D", 1d, 4d),
            ("E", 1d, 0d), ("F", 0d, 24d), ("G", 0d, 22d)
        }));
    }

    [TestCase("1.25em", 1.25, 0)]
    [TestCase("'0.8EM'", 0.8, 0)]
    [TestCase("\"+2.5\"", 1, 2.5)]
    [TestCase("-2", 1, -2)]
    [TestCase("32", 0, 32)]
    public void Rich_sizes_accept_invariant_units_and_quotes(string value, double scale, double offset)
    {
        var token = RichTypewriterTextParser.Parse($"<SIZE = {value}>字</SIZE>").Single();
        Assert.That((token.Text, token.FontSizeScale, token.FontSizeOffset), Is.EqualTo(("字", scale, offset)));
    }

    [TestCase("0")]
    [TestCase("-1em")]
    [TestCase("NaN")]
    [TestCase("Infinity")]
    [TestCase("1e999")]
    [TestCase("+oops")]
    [TestCase("1,5em")]
    [TestCase("12px")]
    [TestCase("'12\"")]
    [TestCase("12 extra")]
    public void Invalid_size_tags_remain_literal(string value)
    {
        var source = $"<size={value}>字</size>";
        Assert.That(string.Concat(RichTypewriterTextParser.Parse(source).Select(token => token.Text)), Is.EqualTo(source));
    }

    [Test]
    public void Size_preserves_other_styles_and_control_directives()
    {
        var tokens = RichTypewriterTextParser.Parse("<size=1.25em><b><i><color=red>A\\d{5}B\\skip C</color></i></b></size>D");
        Assert.That(tokens.Where(token => token.Kind == RichTypewriterTokenKind.Text).Take(3)
            .All(token => token.IsBold && token.IsItalic && token.Color == "red" && token.FontSizeScale == 1.25), Is.True);
        Assert.That(tokens.Single(token => token.Kind == RichTypewriterTokenKind.Delay).DelayMilliseconds, Is.EqualTo(5));
        Assert.That(tokens.Count(token => token.Kind == RichTypewriterTokenKind.SkipBoundary), Is.EqualTo(1));
        Assert.That(tokens[^1].FontSizeScale, Is.EqualTo(1));
    }

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
            Assert.That(tokens.Select(token => token.Kind), Is.EqualTo(ExpectedRichTokenKinds));
            Assert.That(tokens[0], Is.EqualTo(new RichTypewriterToken(RichTypewriterTokenKind.Text, "Bold", IsBold: true)));
            Assert.That(tokens[2], Is.EqualTo(new RichTypewriterToken(RichTypewriterTokenKind.Text, "italic", IsItalic: true)));
            Assert.That(tokens[5].DelayMilliseconds, Is.EqualTo(120));
            Assert.That(tokens[4].Color, Is.EqualTo("#7DD3FC"));
        });
    }

    [Test]
    public void Skip_boundaries_and_backslash_escaping_are_shared_by_both_parsers()
    {
        const string source = "A\\skip<b>B</b>\\\\skip\\skipper";

        var plain = TypewriterTextParser.Parse(source);
        var rich = RichTypewriterTextParser.Parse(source);

        Assert.Multiple(() =>
        {
            Assert.That(plain.Count(token => token.Kind == TypewriterTokenKind.SkipBoundary), Is.EqualTo(1));
            Assert.That(plain.Where(token => token.Kind == TypewriterTokenKind.Text).Select(token => token.Text),
                Is.EqualTo(ExpectedPlainText));
            Assert.That(rich.Count(token => token.Kind == RichTypewriterTokenKind.SkipBoundary), Is.EqualTo(1));
            Assert.That(string.Concat(rich.Where(token => token.Kind == RichTypewriterTokenKind.Text).Select(token => token.Text)),
                Is.EqualTo("AB\\skip\\skipper"));
        });
    }
}
