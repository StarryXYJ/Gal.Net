using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using GalNet.Avalonia.GameView.Composition;
using GalNet.Avalonia.GameView.Navigation;

namespace GalNet.Avalonia.Themes;

/// <summary>Reusable base for startup-configured layouts. Each call creates resources for one player root.</summary>
public abstract class GameLayoutTheme(GamePalette palette) : IGameLayoutTheme
{
    public GamePalette Palette { get; } = palette ?? throw new ArgumentNullException(nameof(palette));
    protected abstract string LayoutName { get; }

    public IStyle CreateStyles()
    {
        var paletteResources = Palette.CreateResources();
        var fluent = new FluentTheme();
        fluent.Palettes[ThemeVariant.Light] = CreateFluentPalette(paletteResources);
        fluent.Palettes[ThemeVariant.Dark] = CreateFluentPalette(paletteResources);
        var styles = new Styles
        {
            // Apply to the player root, not Application, to keep host/editor themes isolated.
            fluent,
            new StyleInclude(new Uri("avares://GalNet.Avalonia.Themes/"))
            {
                Source = new Uri("avares://GalNet.Avalonia.Themes/Common/Styles.axaml")
            }
        };
        styles.Resources.MergedDictionaries.Add(paletteResources);
        var source = new Uri($"avares://GalNet.Avalonia.Themes/Layouts/{LayoutName}/Resources.axaml");
        styles.Resources.MergedDictionaries.Add(new ResourceInclude(source) { Source = source });
        return styles;
    }

    public abstract void RegisterViews(IPageViewRegistryBuilder views);

    private static ColorPaletteResources CreateFluentPalette(IResourceProvider resources)
    {
        Color ColorOf(string name)
        {
            if (resources.TryGetResource($"GalNet.Palette.{name}Color", ThemeVariant.Default, out var value) && value is Color color)
                return color;
            if (resources.TryGetResource($"GalNet.Brush.{name}", ThemeVariant.Default, out value) && value is ISolidColorBrush brush)
                return brush.Color;
            throw new InvalidOperationException($"The Fluent player controls require a solid '{name}' palette color.");
        }

        return new ColorPaletteResources
        {
            Accent = ColorOf("Primary"),
            AltHigh = ColorOf("Background"),
            AltLow = ColorOf("Surface"),
            AltMedium = ColorOf("SurfaceRaised"),
            AltMediumHigh = ColorOf("SurfaceRaised"),
            AltMediumLow = ColorOf("Surface"),
            BaseHigh = ColorOf("TextPrimary"),
            BaseLow = ColorOf("Surface"),
            BaseMedium = ColorOf("TextSecondary"),
            BaseMediumHigh = ColorOf("TextSecondary"),
            BaseMediumLow = ColorOf("TextDisabled"),
            ChromeAltLow = ColorOf("Surface"),
            ChromeBlackHigh = ColorOf("TextPrimary"),
            ChromeBlackLow = ColorOf("Border"),
            ChromeBlackMedium = ColorOf("TextSecondary"),
            ChromeBlackMediumLow = ColorOf("TextDisabled"),
            ChromeDisabledHigh = ColorOf("TextDisabled"),
            ChromeDisabledLow = ColorOf("SurfaceRaised"),
            ChromeGray = ColorOf("Border"),
            ChromeHigh = ColorOf("SurfaceRaised"),
            ChromeLow = ColorOf("Surface"),
            ChromeMedium = ColorOf("SurfaceRaised"),
            ChromeMediumLow = ColorOf("Surface"),
            ChromeWhite = ColorOf("Surface"),
            ErrorText = ColorOf("Error"),
            ListLow = ColorOf("Surface"),
            ListMedium = ColorOf("Selection"),
            RegionColor = ColorOf("Background")
        };
    }
}
