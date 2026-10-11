using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GalNet.Avalonia.GameView.Composition;

/// <summary>Registers default page VMs and their stateful views in one game scope.</summary>
public static class AvaloniaGameViewServiceCollectionExtensions
{
    public static IServiceCollection AddAvaloniaGameViewPages(
        this IServiceCollection services,
        Action<IPageViewRegistryBuilder>? configureViews = null,
        Action<IGalleryPageRegistryBuilder>? configureGalleryPages = null,
        IGameLayoutTheme? theme = null)
    {
        var views = new PageViewRegistryBuilder();
        if (theme is not null) services.AddSingleton(theme);
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
        var galleryPages = new GalleryPageRegistryBuilder(views, services);
        galleryPages.Add<CgGalleryPageViewModel, CgGalleryPage>("cg");
        galleryPages.Add<VideoGalleryPageViewModel, VideoGalleryPage>("video");
        galleryPages.Add<AudioGalleryPageViewModel, AudioGalleryPage>("audio");
        theme?.RegisterViews(views);
        configureGalleryPages?.Invoke(galleryPages);
        configureViews?.Invoke(views);

        foreach (var viewType in views.ViewTypes)
            services.TryAddScoped(viewType);

        var viewRegistry = views.Build();
        var gamePageType = viewRegistry.GetViewType(typeof(GamePageViewModel));
        if (!typeof(GamePage).IsAssignableFrom(gamePageType))
            throw new InvalidOperationException("GamePageViewModel requires a GamePage-derived view. Customize its ControlTheme to replace the layout.");
        services.AddSingleton(viewRegistry);
        services.AddSingleton(galleryPages.Build());
        services.AddScoped<GameNavigationTransitionCoordinator>();
        services.AddScoped<IGameNavigationTransitionCoordinator>(provider =>
            provider.GetRequiredService<GameNavigationTransitionCoordinator>());
        services.AddScoped<IGameNavigationService, GameNavigationService>();
        services.AddScoped<IPlayerSettingsService, DefaultPlayerSettingsService>();
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
        if (gamePageType == typeof(GamePage)) services.AddScoped<GamePage>();
        else services.AddScoped<GamePage>(provider => (GamePage)provider.GetRequiredService(gamePageType));
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
