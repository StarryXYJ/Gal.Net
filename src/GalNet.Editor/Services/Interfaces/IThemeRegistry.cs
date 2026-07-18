using System.Collections.Generic;
using Avalonia.Styling;

namespace GalNet.Editor.Services.Interfaces;

public interface IThemeRegistry
{
    IReadOnlyDictionary<string, ThemeInfo> GetAvailableThemes();
    ThemeVariant? GetThemeVariant(string themeName);
}
