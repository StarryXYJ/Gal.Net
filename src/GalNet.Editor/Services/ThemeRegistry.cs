using System;
using System.Collections.Generic;
using Avalonia.Styling;
using GalNet.Editor.Themes;

namespace GalNet.Editor.Services;

public sealed class ThemeRegistry : IThemeRegistry
{
    private readonly Dictionary<string, ThemeInfo> _themes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Default"] = new("Default", "跟随系统", ThemeVariant.Default),
        ["Light"] = new("Light", "浅色", ThemeVariant.Light),
        ["Dark"] = new("Dark", "深色", ThemeVariant.Dark),
        ["Violet"] = new("Violet", "Violet", EditorThemeVariant.Violet),
        ["Aurora"] = new("Aurora", "Aurora", EditorThemeVariant.Aurora),
    };

    public IReadOnlyDictionary<string, ThemeInfo> GetAvailableThemes() => _themes;

    public ThemeVariant? GetThemeVariant(string themeName) =>
        _themes.TryGetValue(themeName, out var info) ? info.Variant : null;
}
