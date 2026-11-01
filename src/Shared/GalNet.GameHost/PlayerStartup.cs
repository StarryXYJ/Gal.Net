using GalNet.Core.Variable;
using GalNet.Runtime.Variables;
using System.Globalization;

namespace GalNet.GameHost;

/// <summary>Per-game onboarding and successful startup count, outside save-slot scope.</summary>
public static class PlayerStartup
{
    public const string LaunchCountVariable = "player.galnet.startup.launchCount";

    /// <summary>Saved choice wins; otherwise match the system UI language before the project's fallback.</summary>
    public static string ResolvePreferredCulture(IReadOnlyDictionary<string, Variable> stored,
        IReadOnlyList<string> available, string defaultCulture, string systemCulture)
    {
        if (stored.TryGetValue(PlayerPreferences.LocaleVariable, out var locale) && locale.Type == VariableType.String &&
            available.FirstOrDefault(name => name.Equals(locale.AsString(), StringComparison.OrdinalIgnoreCase)) is { } saved)
            return saved;
        try
        {
            var system = CultureInfo.GetCultureInfo(systemCulture);
            for (var culture = system; !string.IsNullOrEmpty(culture.Name); culture = culture.Parent)
                if (available.FirstOrDefault(name => name.Equals(culture.Name, StringComparison.OrdinalIgnoreCase)) is { } exact)
                    return exact;
            if (available.FirstOrDefault(name => CultureInfo.GetCultureInfo(name).TwoLetterISOLanguageName == system.TwoLetterISOLanguageName) is { } related)
                return related;
        }
        catch (CultureNotFoundException) { }
        return available.FirstOrDefault(name => name.Equals(defaultCulture, StringComparison.OrdinalIgnoreCase))
            ?? (available.Count > 0 ? available[0] : throw new InvalidOperationException("No selectable language is available."));
    }

    public static async Task<string?> InitializeAsync(PlayerVariableService variables, string defaultCulture, IReadOnlyList<string> available,
        Func<string, CancellationToken, Task<string?>> selectLanguage, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stored = variables.GetSnapshot(VariableScope.Player);
        var count = stored.TryGetValue(LaunchCountVariable, out var launches) && launches.Type == VariableType.Int
            ? Math.Max(0, launches.AsInt()) : 0;
        var selected = ResolvePreferredCulture(stored, available, defaultCulture, defaultCulture);
        if (count == 0)
        {
            selected = await selectLanguage(selected, cancellationToken);
            if (selected is null) return null;
            if (!available.Contains(selected, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"Selected language '{selected}' is not available.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        var language = new Variable { Name = PlayerPreferences.LocaleVariable };
        language.SetValue(selected);
        var launchCount = new Variable { Name = LaunchCountVariable };
        launchCount.SetValue(count == int.MaxValue ? count : count + 1);
        await variables.UpdatePlayerVariablesAsync(new Dictionary<string, Variable>(StringComparer.Ordinal)
        {
            [language.Name] = language,
            [launchCount.Name] = launchCount
        }, cancellationToken);
        return selected;
    }
}
