using GalNet.Sample.Avalonia.Services;
using GalNet.Sample.Avalonia.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GalNet.Sample.Avalonia.Debug;

/// <summary>Registers the optional in-app debug controls for the Avalonia sample.</summary>
public static class SampleDebugServiceCollectionExtensions
{
    public static IServiceCollection AddSampleDebug(
        this IServiceCollection services,
        SampleDebugLogStore? logStore = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        logStore ??= SampleDebugLogStore.Shared;
        logStore.Enable();
        services.TryAddSingleton<SampleDebugLogStore>(logStore);
        services.TryAddSingleton<SampleDebugLogDirectory>();
        services.TryAddScoped<SampleDebugActions>();
        services.TryAddScoped<SampleDebugViewModel>();
        services.TryAddTransient<DebugLogWindow>();
        return services;
    }
}

/// <summary>Debug-only orchestration kept outside the reusable game pages.</summary>
internal sealed class SampleDebugActions(
    SampleGameSessionService session,
    SampleDebugLogDirectory logDirectory)
{
    public async Task ClearPlayerStateAsync(CancellationToken cancellationToken = default)
    {
        await session.ClearPlayerStateAsync(cancellationToken);
        SampleApplicationRestarter.Restart();
    }

    public async Task ReloadGameResourcesAsync(CancellationToken cancellationToken = default)
    {
        await session.StopAsync(cancellationToken);
        SampleApplicationRestarter.Restart();
    }

    public Task<SampleDebugSaveResult> SaveDebugSnapshotAsync(
        SampleDebugSaveKind kind,
        bool overwriteWhenAllVisibleSlotsAreOccupied = false,
        CancellationToken cancellationToken = default) =>
        session.SaveDebugSnapshotAsync(kind, overwriteWhenAllVisibleSlotsAreOccupied, cancellationToken);

    public void OpenLogDirectory() => logDirectory.Open();
}
