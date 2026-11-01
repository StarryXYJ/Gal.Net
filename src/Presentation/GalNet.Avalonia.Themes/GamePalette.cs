using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;

namespace GalNet.Avalonia.Themes;

/// <summary>Complete semantic player colors, independent of the page layout.</summary>
public sealed class GamePalette(Uri source)
{
    public static GamePalette Ocean { get; } = Builtin("Ocean");
    public static GamePalette Rose { get; } = Builtin("Rose");
    public static GamePalette Mono { get; } = Builtin("Mono");

    public Uri Source { get; } = source ?? throw new ArgumentNullException(nameof(source));

    public static IReadOnlyList<string> RequiredBrushes { get; } = Array.AsReadOnly(new[]
    {
        "Primary", "PrimaryHover", "PrimaryPressed", "OnPrimary", "Secondary", "OnSecondary",
        "Background", "Surface", "SurfaceRaised", "Overlay", "TextPrimary", "TextSecondary", "TextDisabled",
        "Border", "Divider", "Focus", "Selection", "Success", "Warning", "Error", "Info"
    });

    public IResourceProvider CreateResources()
    {
        var resources = new ResourceInclude(Source) { Source = Source };
        foreach (var name in RequiredBrushes)
        {
            var key = $"GalNet.Brush.{name}";
            if (!resources.TryGetResource(key, ThemeVariant.Default, out var value) || value is not IBrush)
                throw new InvalidOperationException($"Palette '{Source}' must provide the IBrush resource '{key}'.");
        }
        return resources;
    }

    private static GamePalette Builtin(string name) => new(new Uri($"avares://GalNet.Avalonia.Themes/Palettes/{name}.axaml"));
    public global::Avalonia.Themes.Fluent.ColorPaletteResources CreateFluentPalette()
    {
        var resources = CreateResources();
        Color ColorOf(string name)
        {
            if (resources.TryGetResource($"GalNet.Palette.{name}Color", ThemeVariant.Default, out var value) && value is Color color)
                return color;
            if (resources.TryGetResource($"GalNet.Brush.{name}", ThemeVariant.Default, out value) && value is ISolidColorBrush brush)
                return brush.Color;
            throw new InvalidOperationException($"The Fluent player controls require a solid '{name}' palette color.");
        }

        return new global::Avalonia.Themes.Fluent.ColorPaletteResources
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
