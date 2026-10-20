using GalNet.Core.I18n;

namespace GeneralTest.I18n;

public class I18nKeyTests
{
    [Test]
    public void Key_Should_Be_Equatable()
    {
        var k1 = new I18nKey("group_01.entry_03.content");
        var k2 = new I18nKey("group_01.entry_03.content");
        var k3 = new I18nKey("group_01.entry_04.content");

        Assert.That(k1, Is.EqualTo(k2));
        Assert.That(k1, Is.Not.EqualTo(k3));
        Assert.That(k1 == k2, Is.True);
        Assert.That(k1 != k3, Is.True);
    }

    [Test]
    public void ForEntry_Should_Generate_Expected_Format()
    {
        var key = I18nKey.ForEntry("group_abc", "entry-3", "content");
        Assert.That(key.Key, Is.EqualTo("group.group_abc.entry.entry-3.content"));
    }

    [Test]
    public void ForBranchOption_Should_Generate_Expected_Format()
    {
        var key = I18nKey.ForBranchOption("branch_01", 2, "text");
        Assert.That(key.Key, Is.EqualTo("branch_01.option_2.text"));
    }

    [Test]
    public void ToString_Should_Return_Key()
    {
        var key = new I18nKey("test.key");
        Assert.That(key.ToString(), Is.EqualTo("test.key"));
    }
}

public class I18nLocaleTests
{
    [TestCase(" ZH_hans_cn ", "zh-Hans-CN")]
    [TestCase("EN-us", "en-US")]
    [TestCase("sr_latn_rs", "sr-Latn-RS")]
    public void Locale_Uses_Canonical_Culture_Casing(string input, string expected) =>
        Assert.That(new I18nLocale(input).Code, Is.EqualTo(expected));

    [Test]
    public void Locale_Rejects_Invalid_Culture() => Assert.Throws<System.Globalization.CultureNotFoundException>(() => new I18nLocale("bad/locale"));

    [Test]
    public void Locale_Should_Be_Equatable()
    {
        Assert.That(I18nLocale.ZhCn, Is.EqualTo(new I18nLocale("zh-CN")));
        Assert.That(I18nLocale.EnUs, Is.EqualTo(new I18nLocale("en-US")));
    }

    [Test]
    public void Locale_Should_Normalize_Underscore_To_Dash()
    {
        var locale = new I18nLocale("zh_cn");
        Assert.That(locale.Code, Is.EqualTo("zh-CN"));
    }

    [Test]
    public void Locale_Should_Keep_Dash_Format()
    {
        var locale = new I18nLocale("ja-JP");
        Assert.That(locale.Code, Is.EqualTo("ja-JP"));
    }

    [Test]
    public void ToCultureInfo_Should_Return_CultureInfo()
    {
        var ci = I18nLocale.ZhCn.ToCultureInfo();
        Assert.That(ci.Name, Is.EqualTo("zh-CN"));
    }
}
