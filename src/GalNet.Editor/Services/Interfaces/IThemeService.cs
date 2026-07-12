namespace GalNet.Editor.Services;

public interface IThemeService
{
    void ApplyThemeByName(string themeName);
    string GetCurrentThemeName();
}
