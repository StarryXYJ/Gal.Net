using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using GalNet.Avalonia.Themes;

namespace GeneralTest.Presentation;

[NonParallelizable]
public sealed class GameTypographyTests
{
    [Test]
    public void HostsGetIndependentFallbackOptions()
    {
        var first = GameTypography.CreateFontManagerOptions();
        var second = GameTypography.CreateFontManagerOptions();
        Assert.That(first, Is.Not.SameAs(second));
        Assert.That(first.FontFallbacks, Is.Not.SameAs(second.FontFallbacks));
        Assert.That(first.FontFallbacks!.Select(item => item.FontFamily.Name),
            Does.Contain("Noto Sans CJK SC").And.Contain("Source Han Sans SC")
                .And.Not.Contain("Microsoft YaHei UI").And.Not.Contain("Microsoft YaHei").And.Not.Contain("PingFang SC"));
        Assert.That(first.DefaultFamilyName, Is.Null);
        Assert.That(GameTypography.UiFontFamily, Is.EqualTo(FontFamily.Default));
        Assert.That(GameTypography.CreateFontManagerOptions("fonts:Inter#Inter").DefaultFamilyName,
            Is.EqualTo("fonts:Inter#Inter"));
    }

    [Test]
    public Task ThemesShareTypographyAndRenderChineseInRegularAndBold() =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly).Dispatch(() =>
        {
            const string sample = "游戏设置 · 快捷键绑定，存档与鉴赏。中文测试 ABC 123";
            foreach (var theme in new GameLayoutTheme[] { new ClassicTheme(), new MinimalTheme(), new PaperTheme() })
            {
                var text = new TextBlock { Text = sample, FontSize = 24, Margin = new(24), TextWrapping = TextWrapping.Wrap };
                var bold = new TextBlock { Text = sample, FontSize = 24, FontWeight = FontWeight.Bold, Margin = new(24), TextWrapping = TextWrapping.Wrap };
                var root = new Border { Classes = { "galnet-player" }, Child = new StackPanel { Children = { text, bold } } };
                root.Styles.Add(theme.CreateStyles());
                var window = new Window { Content = root, Width = 1100, Height = 220 };
                window.Show();
                try
                {
                    window.UpdateLayout();
                    Assert.That(root.TryFindResource("GalNet.Theme.FontFamily", out var family), Is.True);
                    Assert.That(family, Is.SameAs(GameTypography.UiFontFamily));
                    Assert.That(text.FontFamily, Is.EqualTo(GameTypography.UiFontFamily));
                    foreach (var weight in new[] { FontWeight.Normal, FontWeight.Bold })
                    {
                        foreach (var codepoint in "中文游戏设置，。")
                        {
                            Assert.That(FontManager.Current.TryMatchCharacter(codepoint, FontStyle.Normal, weight, FontStretch.Normal,
                                GameTypography.UiFontFamily, CultureInfo.GetCultureInfo("zh-CN"), out var face), Is.True);
                            Assert.That(face.GlyphTypeface.CharacterToGlyphMap.GetGlyph(codepoint), Is.Not.Zero,
                                $"Missing glyph U+{(int)codepoint:X4} in {face.FontFamily.Name}");
                        }
                    }
                    TestContext.Progress.WriteLine($"{theme.GetType().Name}: {new Typeface(text.FontFamily).GlyphTypeface.FamilyName}");
                    var directory = Environment.GetEnvironmentVariable("GALNET_THEME_CAPTURE_DIR");
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        root.TryFindResource("GalNet.Brush.Background", out var background);
                        root.Background = (IBrush)background!;
                        Directory.CreateDirectory(directory);
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        using var bitmap = window.CaptureRenderedFrame();
                        bitmap!.Save(Path.Combine(directory, theme.GetType().Name + "-ChineseTypography.png"));
                    }
                }
                finally { window.Close(); }
            }
            Dispatcher.UIThread.RunJobs();
            return true;
        }, TestContext.CurrentContext.CancellationToken);
}
