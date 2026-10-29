using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Media.LibVlc;
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
        services.TryAddSingleton(_ => LibVlcFactory.Default);
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
        services.TryAddScoped<GameNavigationTransitionCoordinator>();
        services.TryAddScoped<IGameNavigationTransitionCoordinator>(provider =>
            provider.GetRequiredService<GameNavigationTransitionCoordinator>());
        services.TryAddScoped<IGameNavigationService, GameNavigationService>();
        services.TryAddScoped<IPlayerSettingsService, DefaultPlayerSettingsService>();
        services.TryAddScoped<GalleryNavigationService>();
        services.TryAddScoped<GameLaunchFlow>();
        services.TryAddScoped<IPageViewFactory, PageViewFactory>();
        services.TryAddScoped<IGameScreenshotService, AvaloniaGameScreenshotService>();
        services.TryAddScoped<GalleryMediaService>();
        services.TryAddScoped<GameShellViewModel>();
        services.TryAddScoped<TitlePageViewModel>();
        services.TryAddScoped<GamePageViewModel>();
        services.TryAddScoped<SaveSlotsPageViewModel>();
        services.TryAddScoped<SettingsPageViewModel>();
        services.TryAddScoped<GalleryPageViewModel>();
        services.TryAddScoped<CgGalleryImagePageViewModel>();
        services.TryAddScoped<VideoGalleryPlayerPageViewModel>();
        services.TryAddScoped<MissingGalleryPageViewModel>();
        services.TryAddScoped<AboutPageViewModel>();
        services.TryAddScoped<LoadingPageViewModel>();
        services.TryAddScoped<GameShell>();

        if (gamePageType == typeof(GamePage)) services.TryAddScoped<GamePage>();
        else services.TryAddScoped<GamePage>(provider => (GamePage)provider.GetRequiredService(gamePageType));
        return services;
    }
}
