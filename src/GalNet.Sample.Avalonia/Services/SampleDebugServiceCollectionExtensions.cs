using GalNet.Sample.Avalonia.Views;
using GalNet.Sample.Avalonia.Services;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.ViewModels;
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
        services.TryAddScoped<SampleDebugActions>();
        services.TryAddTransient<DebugLogWindow>();
        return services;
    }
}

/// <summary>Debug-only orchestration kept outside the reusable game pages.</summary>
internal sealed class SampleDebugActions(
    SampleGameSessionService session,
    IGameNavigationService navigation)
{
    public async Task ClearPlayerStateAsync(CancellationToken cancellationToken = default)
    {
        await ReturnToTitleAsync(cancellationToken);
        await session.ClearPlayerStateAsync(cancellationToken);
    }

    public async Task ReloadGameResourcesAsync(CancellationToken cancellationToken = default)
    {
        await ReturnToTitleAsync(cancellationToken);
        await session.ReloadGameResourcesAsync(cancellationToken);
    }

    private Task ReturnToTitleAsync(CancellationToken cancellationToken) =>
        navigation.ResetToAsync<TitlePageViewModel>(NavigationTransition.None, cancellationToken);
}
