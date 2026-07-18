namespace GalNet.Editor.Services.Interfaces;

public interface IThemeService
{
    void ApplyThemeByName(string themeName);
    string GetCurrentThemeName();
}
