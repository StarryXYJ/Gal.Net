using GalNet.GameHost;
using GalNet.Runtime.Variables;

namespace GalNet.Avalonia.GameHost;

/// <summary>Connects the default player localization adapter to neutral onboarding.</summary>
public sealed class AvaloniaPlayerStartup(IPlayerLocalization localization)
{
    public const string LaunchCountVariable = PlayerStartup.LaunchCountVariable;
    public async Task<bool> InitializeAsync(PlayerVariableService variables, string defaultCulture,
        Func<string, CancellationToken, Task<string?>> selectLanguage, CancellationToken cancellationToken = default)
    {
        var selected = await PlayerStartup.InitializeAsync(variables, defaultCulture,
            localization.CultureService.AvailableCultures.Select(culture => culture.Name).ToArray(), selectLanguage, cancellationToken);
        if (selected is null) return false;
        localization.ApplyCulture(selected);
        return true;
    }
}
