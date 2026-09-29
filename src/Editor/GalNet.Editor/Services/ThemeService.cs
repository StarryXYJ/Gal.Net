using Avalonia;

namespace GalNet.Editor.Services;

public sealed class ThemeService : IThemeService
{
    private readonly IThemeRegistry _themeRegistry;

    public ThemeService(IThemeRegistry themeRegistry)
    {
        _themeRegistry = themeRegistry;
    }

    public void ApplyThemeByName(string themeName)
    {
        if (Application.Current is null) return;

        var variant = _themeRegistry.GetThemeVariant(themeName)
                      ?? _themeRegistry.GetThemeVariant("Violet");
        if (variant is not null)
            Application.Current.RequestedThemeVariant = variant;
    }

    public string GetCurrentThemeName()
    {
        if (Application.Current is null) return "Violet";

        var variant = Application.Current.RequestedThemeVariant;
        foreach (var (_, info) in _themeRegistry.GetAvailableThemes())
        {
            if (Equals(info.Variant, variant))
                return info.Name;
        }

        return "Violet";
    }
}
