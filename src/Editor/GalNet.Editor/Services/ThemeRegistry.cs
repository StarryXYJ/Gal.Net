using System;
using System.Collections.Generic;
using Avalonia.Styling;
using GalNet.Editor.Themes;

namespace GalNet.Editor.Services;

public sealed class ThemeRegistry : IThemeRegistry
{
    private readonly Dictionary<string, ThemeInfo> _themes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Default"] = new("Default", "Theme.Default", ThemeVariant.Default),
        ["Light"] = new("Light", "Theme.Light", ThemeVariant.Light),
        ["Dark"] = new("Dark", "Theme.Dark", ThemeVariant.Dark),
        ["Violet"] = new("Violet", "Theme.Violet", EditorThemeVariant.Violet),
        ["Aurora"] = new("Aurora", "Theme.Aurora", EditorThemeVariant.Aurora),
    };

    public IReadOnlyDictionary<string, ThemeInfo> GetAvailableThemes() => _themes;

    public ThemeVariant? GetThemeVariant(string themeName) =>
        _themes.TryGetValue(themeName, out var info) ? info.Variant : null;
}
