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
        Action<IPageViewRegistryBuilder>? configureViews = null,
        Action<IGalleryPageRegistryBuilder>? configureGalleryPages = null)
    {
        var views = new PageViewRegistryBuilder();
        views.Register<TitlePageViewModel, TitlePage>();
        views.Register<GamePageViewModel, GamePage>();
        views.Register<SaveSlotsPageViewModel, SaveSlotsPage>();
        views.Register<SettingsPageViewModel, SettingsPage>();
        views.Register<GalleryPageViewModel, GalleryPage>();
        views.Register<CgGalleryImagePageViewModel, CgGalleryImagePage>();
        views.Register<VideoGalleryPlayerPageViewModel, VideoGalleryPlayerPage>();
        views.Register<MissingGalleryPageViewModel, MissingGalleryPage>();
        views.Register<AboutPageViewModel, AboutPage>();
        views.Register<LoadingPageViewModel, LoadingPage>();
        configureViews?.Invoke(views);

        var galleryPages = new GalleryPageRegistryBuilder(views, services);
        galleryPages.Add<CgGalleryPageViewModel, CgGalleryPage>("cg");
        galleryPages.Add<VideoGalleryPageViewModel, VideoGalleryPage>("video");
        galleryPages.Add<AudioGalleryPageViewModel, AudioGalleryPage>("audio");
        configureGalleryPages?.Invoke(galleryPages);

        services.AddSingleton(views.Build());
        services.AddSingleton(galleryPages.Build());
        services.AddScoped<GameNavigationTransitionCoordinator>();
        services.AddScoped<IGameNavigationTransitionCoordinator>(provider =>
            provider.GetRequiredService<GameNavigationTransitionCoordinator>());
        services.AddScoped<IGameNavigationService, GameNavigationService>();
        services.AddScoped<GalleryNavigationService>();
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
        services.AddScoped<CgGalleryImagePageViewModel>();
        services.AddScoped<VideoGalleryPlayerPageViewModel>();
        services.AddScoped<MissingGalleryPageViewModel>();
        services.AddScoped<AboutPageViewModel>();
        services.AddScoped<LoadingPageViewModel>();

        services.AddScoped<GameShell>();
        services.AddScoped<TitlePage>();
        services.AddScoped<GamePage>();
        services.AddScoped<SaveSlotsPage>();
        services.AddScoped<SettingsPage>();
        services.AddScoped<GalleryPage>();
        services.AddScoped<CgGalleryImagePage>();
        services.AddScoped<VideoGalleryPlayerPage>();
        services.AddScoped<MissingGalleryPage>();
        services.AddScoped<AboutPage>();
        services.AddScoped<LoadingPage>();
        return services;
    }
}
