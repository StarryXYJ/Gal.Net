using GalNet.Avalonia.Debugging.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GalNet.Avalonia.Debugging;

public static class GameDebugServiceCollectionExtensions
{
    public static IServiceCollection AddGameDebugTools(this IServiceCollection services,
        GameDebugHostOptions options, GameDebugLogStore? logStore = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        logStore ??= GameDebugLogStore.Shared;
        logStore.Enable();
        services.TryAddSingleton(logStore);
        services.TryAddScoped(_ => options);
        services.TryAddScoped<GameDebugActions>();
        services.TryAddScoped<GameDebugViewModel>();
        services.TryAddScoped<AvaloniaExternalDebugSession>();
        services.TryAddTransient<DebugLogView>();
        services.TryAddTransient<DebugLogWindow>();
        services.TryAddScoped<DebugToolsOverlay>();
        return services;
    }
}
