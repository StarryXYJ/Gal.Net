using GalNet.Avalonia.Themes;
using GalNet.Sample.CrossPlatform;
namespace GeneralTest.Presentation;

internal sealed class TemplateAppearance(GamePalette? palette = null)
{
    public GamePalette Palette => palette ?? GamePalette.Mono;
    public global::Avalonia.Styling.IStyle CreateStyles() => SampleCustomization.CreateStyles(palette);
}
