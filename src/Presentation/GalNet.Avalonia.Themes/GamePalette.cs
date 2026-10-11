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
}
