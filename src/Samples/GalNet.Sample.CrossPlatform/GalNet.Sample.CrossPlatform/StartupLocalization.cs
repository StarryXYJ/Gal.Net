using GalNet.Avalonia.GameHost;
using GalNet.GameHost;
using GalNet.Storage.FileSystem;

namespace GalNet.Sample.CrossPlatform;

/// <summary>Reads only the UI preference, before game resources or the player session are ready.</summary>
public static class StartupLocalization
{
    public static void ApplySystemCulture(AvaloniaPlayerLocalization localization, string systemCulture) =>
        localization.ApplyCulture(PlayerStartup.ResolvePreferredCulture(new Dictionary<string, GalNet.Core.Variable.Variable>(),
            localization.CultureService.AvailableCultures.Select(culture => culture.Name).ToArray(), "en-US", systemCulture));

    public static async Task ApplyProfileAsync(AvaloniaPlayerLocalization localization, string profileDirectory,
        string systemCulture, CancellationToken cancellationToken = default)
    {
        using var store = new FilePlayerVariableStore(profileDirectory);
        var stored = await store.LoadAsync(cancellationToken);
        localization.ApplyCulture(PlayerStartup.ResolvePreferredCulture(stored,
            localization.CultureService.AvailableCultures.Select(culture => culture.Name).ToArray(), "en-US", systemCulture));
    }

    public static async Task ApplyProfileWithRecoveryAsync(AvaloniaPlayerLocalization localization, string profileDirectory,
        string systemCulture, Func<CancellationToken, Task<bool>> confirmRecovery, CancellationToken cancellationToken = default)
    {
        try { await ApplyProfileAsync(localization, profileDirectory, systemCulture, cancellationToken); }
        catch (CorruptPlayerVariablesException error)
        {
            if (!await confirmRecovery(cancellationToken)) throw new OperationCanceledException("Player data recovery was declined.");
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(error.FilePath, error.FilePath + ".corrupt-" + Guid.NewGuid().ToString("N") + ".bak");
        }
    }
}
