using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

[TestFixture]
public class VariableNameRulesTests
{
    [TestCase("player_score", true)]
    [TestCase("_temp", true)]
    [TestCase("Player123", true)]
    [TestCase("123Player", true)]
    [TestCase("player score", false)]
    [TestCase("变量名_test", false)]
    [TestCase("name-", false)]
    public void IsValid_UsesAsciiLettersDigitsAndUnderscoreRule(string input, bool expected)
    {
        Assert.That(VariableNameRules.IsValid(input), Is.EqualTo(expected));
    }

    [Test]
    public void Sanitize_RemovesSpacesAndNonAsciiCharacters()
    {
        var sanitized = VariableNameRules.Sanitize("变量 名_test 123", "var");

        Assert.That(sanitized, Is.EqualTo("_test123"));
    }
}
