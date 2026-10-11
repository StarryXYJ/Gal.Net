using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GalNet.Avalonia.GameView.Composition;

/// <summary>Registers shared behavior and theme-owned views in one game scope.</summary>
public static class AvaloniaGameViewServiceCollectionExtensions
{
    public static IServiceCollection AddAvaloniaGameViewPages(
        this IServiceCollection services,
        IGameLayoutTheme theme,
        Action<IPageViewRegistryBuilder>? configureViews = null,
        Action<IGalleryPageRegistryBuilder>? configureGalleryPages = null)
    {
        var views = new PageViewRegistryBuilder();
        ArgumentNullException.ThrowIfNull(theme);
        services.AddSingleton(theme);
        views.Register<GamePageViewModel, GamePage>();
        var galleryPages = new GalleryPageRegistryBuilder(views, services);

        galleryPages.Add<CgGalleryPageViewModel>("cg");
        galleryPages.Add<VideoGalleryPageViewModel>("video");
        galleryPages.Add<AudioGalleryPageViewModel>("audio");

        theme.RegisterViews(views);
        configureGalleryPages?.Invoke(galleryPages);
        configureViews?.Invoke(views);

        var viewRegistry = views.Build();
        var requiredViewModels = new[]
        {
            typeof(TitlePageViewModel), typeof(GamePageViewModel), typeof(SettingsPageViewModel),
            typeof(SaveSlotsPageViewModel), typeof(GalleryPageViewModel), typeof(CgGalleryPageViewModel),
            typeof(CgGalleryImagePageViewModel), typeof(AudioGalleryPageViewModel), typeof(VideoGalleryPageViewModel),
            typeof(VideoGalleryPlayerPageViewModel), typeof(MissingGalleryPageViewModel), typeof(AboutPageViewModel),
            typeof(LoadingPageViewModel), typeof(ScreenshotDialogViewModel)
        };
        var missing = new List<string>();
        foreach (var viewModelType in requiredViewModels)
        {
            try { viewRegistry.GetViewType(viewModelType); }
            catch (InvalidOperationException) { missing.Add(viewModelType.Name); }
        }

        if (missing.Count > 0)
            throw new InvalidOperationException($"The layout theme and host must register all required views. Missing: {string.Join(", ", missing)}.");
        if (!typeof(ScreenshotDialog).IsAssignableFrom(viewRegistry.GetViewType(typeof(ScreenshotDialogViewModel))))
            throw new InvalidOperationException("ScreenshotDialogViewModel requires a ScreenshotDialog-derived view.");
        var gamePageType = viewRegistry.GetViewType(typeof(GamePageViewModel));
        if (!typeof(GamePage).IsAssignableFrom(gamePageType))
            throw new InvalidOperationException("GamePageViewModel requires a GamePage-derived view. Customize its ControlTheme to replace the layout.");

        foreach (var viewType in views.ViewTypes)
        {
            if (typeof(ScreenshotDialog).IsAssignableFrom(viewType)) continue;
            services.TryAddScoped(viewType);
        }
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

        if (gamePageType == typeof(GamePage)) services.AddScoped<GamePage>();
        else services.AddScoped<GamePage>(provider => (GamePage)provider.GetRequiredService(gamePageType));
        return services;
    }
}
