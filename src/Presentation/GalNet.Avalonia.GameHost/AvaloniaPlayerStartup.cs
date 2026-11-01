using GalNet.GameHost;
using GalNet.Runtime.Variables;
using GalNet.Core.Variable;

namespace GalNet.Avalonia.GameHost;

/// <summary>Connects the default player localization adapter to neutral onboarding.</summary>
public sealed class AvaloniaPlayerStartup(IPlayerLocalization localization) : IPlayerStartup
{
    public const string LaunchCountVariable = PlayerStartup.LaunchCountVariable;
    public async Task<bool> InitializeAsync(PlayerVariableService variables, string defaultCulture,
        Func<string, CancellationToken, Task<string?>> selectLanguage, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var available = localization.CultureService.AvailableCultures.Select(culture => culture.Name).ToArray();
        var preferred = PlayerStartup.ResolvePreferredCulture(variables.GetSnapshot(VariableScope.Player),
            available, defaultCulture, localization.CultureService.CurrentCulture.Name);
        var previous = localization.CultureService.CurrentCulture.Name;
        localization.ApplyCulture(preferred);
        try
        {
            var selected = await PlayerStartup.InitializeAsync(variables, preferred, available, selectLanguage, cancellationToken);
            if (selected is null) { localization.ApplyCulture(previous); return false; }
            localization.ApplyCulture(selected);
            return true;
        }
        catch
        {
            localization.ApplyCulture(previous);
            throw;
        }
    }
}
