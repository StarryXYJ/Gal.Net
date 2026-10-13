using GalNet.Runtime.Variables;
using GalNet.Core.Variable;

namespace GalNet.Sample.Avalonia.Services;

/// <summary>Per-game onboarding and successful startup count, outside save-slot scope.</summary>
public sealed class SampleStartupService(SampleLocalizationService localization)
{
    public const string LaunchCountVariable = "player.galnet.startup.launchCount";

    public async Task<bool> InitializeAsync(PlayerVariableService variables, string defaultCulture,
        Func<string, CancellationToken, Task<string?>> selectLanguage, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stored = variables.GetSnapshot(VariableScope.Player);
        var count = stored.TryGetValue(LaunchCountVariable, out var launches) && launches.Type == VariableType.Int
            ? Math.Max(0, launches.AsInt()) : 0;
        var available = localization.CultureService.AvailableCultures.Select(culture => culture.Name).ToArray();
        var selected = stored.TryGetValue(SamplePlayerSettingsService.LocaleVariable, out var locale) ? locale.AsString() : defaultCulture;
        if (!available.Contains(selected, StringComparer.OrdinalIgnoreCase))
            selected = available.FirstOrDefault(culture => culture.Equals(defaultCulture, StringComparison.OrdinalIgnoreCase))
                ?? available.FirstOrDefault() ?? throw new InvalidOperationException("No selectable language is available.");
        if (count == 0)
        {
            selected = await selectLanguage(selected, cancellationToken);
            if (selected is null) return false;
            if (!available.Contains(selected, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"Selected language '{selected}' is not available.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        var language = new Variable { Name = SamplePlayerSettingsService.LocaleVariable };
        language.SetValue(selected);
        var launchCount = new Variable { Name = LaunchCountVariable };
        launchCount.SetValue(count == int.MaxValue ? count : count + 1);
        await variables.UpdatePlayerVariablesAsync(new Dictionary<string, Variable>(StringComparer.Ordinal)
        {
            [language.Name] = language,
            [launchCount.Name] = launchCount
        }, cancellationToken);
        localization.ApplyCulture(selected);
        return true;
    }
}
