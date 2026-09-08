using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Avalonia.GameView.Composition;

/// <summary>Registers default page VMs and their stateful views in one game scope.</summary>
public static class AvaloniaGameViewServiceCollectionExtensions
{
    public static IServiceCollection AddAvaloniaGameViewPages(this IServiceCollection services)
    {
        services.AddScoped<IGameNavigationService, GameNavigationService>();
        services.AddScoped<IPageViewFactory, PageViewFactory>();
        services.AddScoped<GameShellViewModel>();
        services.AddScoped<TitlePageViewModel>();
        services.AddScoped<GamePageViewModel>();
        services.AddScoped<SaveSlotsPageViewModel>();
        services.AddScoped<SettingsPageViewModel>();
        services.AddScoped<GalleryPageViewModel>();
        services.AddScoped<AboutPageViewModel>();

        services.AddScoped<GameShell>();
        services.AddScoped<TitlePage>();
        services.AddScoped<GamePage>();
        services.AddScoped<SaveSlotsPage>();
        services.AddScoped<SettingsPage>();
        services.AddScoped<GalleryPage>();
        services.AddScoped<AboutPage>();
        return services;
    }
}
