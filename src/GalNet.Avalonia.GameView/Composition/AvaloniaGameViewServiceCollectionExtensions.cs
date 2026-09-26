using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Avalonia.GameView.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Avalonia.GameView.Composition;

/// <summary>Registers default page VMs and their stateful views in one game scope.</summary>
public static class AvaloniaGameViewServiceCollectionExtensions
{
    public static IServiceCollection AddAvaloniaGameViewPages(
        this IServiceCollection services,
        Action<IPageViewRegistryBuilder>? configureViews = null)
    {
        var views = new PageViewRegistryBuilder();
        views.Register<TitlePageViewModel, TitlePage>();
        views.Register<GamePageViewModel, GamePage>();
        views.Register<SaveSlotsPageViewModel, SaveSlotsPage>();
        views.Register<SettingsPageViewModel, SettingsPage>();
        views.Register<GalleryPageViewModel, GalleryPage>();
        views.Register<GalleryContentPageViewModel, GalleryContentPage>();
        views.Register<GalleryMediaPageViewModel, GalleryMediaPage>();
        views.Register<AboutPageViewModel, AboutPage>();
        views.Register<LoadingPageViewModel, LoadingPage>();
        configureViews?.Invoke(views);

        services.AddSingleton(views.Build());
        services.AddScoped<GameNavigationTransitionCoordinator>();
        services.AddScoped<IGameNavigationTransitionCoordinator>(provider =>
            provider.GetRequiredService<GameNavigationTransitionCoordinator>());
        services.AddScoped<IGameNavigationService, GameNavigationService>();
        services.AddScoped<GameLaunchFlow>();
        services.AddScoped<IPageViewFactory, PageViewFactory>();
        services.AddScoped<IGameScreenshotService, AvaloniaGameScreenshotService>();
        services.AddScoped<GalleryMediaService>();
        services.AddScoped<GameShellViewModel>();
        services.AddScoped<TitlePageViewModel>();
        services.AddScoped<GamePageViewModel>();
        services.AddScoped<SaveSlotsPageViewModel>();
        services.AddScoped<SettingsPageViewModel>();
        services.AddScoped<GalleryPageViewModel>();
        services.AddScoped<GalleryContentPageViewModel>();
        services.AddScoped<GalleryMediaPageViewModel>();
        services.AddScoped<AboutPageViewModel>();
        services.AddScoped<LoadingPageViewModel>();

        services.AddScoped<GameShell>();
        services.AddScoped<TitlePage>();
        services.AddScoped<GamePage>();
        services.AddScoped<SaveSlotsPage>();
        services.AddScoped<SettingsPage>();
        services.AddScoped<GalleryPage>();
        services.AddScoped<GalleryContentPage>();
        services.AddScoped<GalleryMediaPage>();
        services.AddScoped<AboutPage>();
        services.AddScoped<LoadingPage>();
        return services;
    }
}
