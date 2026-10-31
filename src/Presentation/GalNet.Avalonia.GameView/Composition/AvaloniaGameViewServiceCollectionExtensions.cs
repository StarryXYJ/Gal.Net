using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Media.LibVlc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GalNet.Avalonia.GameView.Composition;

/// <summary>Registers reusable page behavior and application view mappings in one game scope.</summary>
public static class AvaloniaGameViewServiceCollectionExtensions
{
    /// <summary>Registers reusable page mechanisms and only the views explicitly chosen by the application.</summary>
    public static IServiceCollection AddAvaloniaGameView(this IServiceCollection services,
        Action<IPageViewRegistryBuilder> configureViews,
        Action<IGalleryPageRegistryBuilder>? configureGalleryPages = null)
    {
        var views = new PageViewRegistryBuilder();
        var galleries = new GalleryPageRegistryBuilder(views, services);
        configureViews(views);
        configureGalleryPages?.Invoke(galleries);
        var registry = views.Build();
        foreach (var model in views.ViewModelTypes)
        {
            var view = registry.GetViewType(model);
            if (typeof(IGameplayPage).IsAssignableFrom(model) && !typeof(GamePage).IsAssignableFrom(view))
                throw new InvalidOperationException($"Gameplay page '{model.FullName}' requires a GamePage-derived view; registered '{view.FullName}'.");
            if (model == typeof(ScreenshotDialogViewModel) && !typeof(ScreenshotDialog).IsAssignableFrom(view))
                throw new InvalidOperationException($"ScreenshotDialogViewModel requires a ScreenshotDialog-derived view; registered '{view.FullName}'.");
        }
        services.AddSingleton(registry);
        services.AddSingleton(galleries.Build());
        foreach (var viewModelType in views.ViewModelTypes)
            if (typeof(PageViewModelBase).IsAssignableFrom(viewModelType)) services.TryAddScoped(viewModelType);
        foreach (var viewType in views.ViewTypes)
            if (!typeof(ScreenshotDialog).IsAssignableFrom(viewType)) services.TryAddScoped(viewType);
        services.TryAddSingleton(_ => LibVlcFactory.Default);
        services.TryAddScoped<GameNavigationTransitionCoordinator>();
        services.TryAddScoped<IGameNavigationTransitionCoordinator>(provider => provider.GetRequiredService<GameNavigationTransitionCoordinator>());
        services.TryAddScoped<IGameNavigationService, GameNavigationService>();
        services.TryAddScoped<IPageViewFactory, PageViewFactory>();
        services.TryAddScoped<IPlayerSettingsService, DefaultPlayerSettingsService>();
        services.TryAddScoped<IGameScreenshotService, AvaloniaGameScreenshotService>();
        services.TryAddScoped<GalleryMediaService>();
        services.TryAddScoped<GameShellViewModel>();
        return services;
    }

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
        galleryPages.Add<SceneGalleryPageViewModel>("scene");

        theme.RegisterViews(views);
        configureGalleryPages?.Invoke(galleryPages);
        configureViews?.Invoke(views);

        var viewRegistry = views.Build();
        var requiredViewModels = new[]
        {
            typeof(TitlePageViewModel), typeof(GamePageViewModel), typeof(SettingsPageViewModel),
            typeof(SaveSlotsPageViewModel), typeof(GalleryPageViewModel), typeof(CgGalleryPageViewModel),
            typeof(SceneGalleryPageViewModel), typeof(SceneGalleryPlayerPageViewModel), typeof(CgGalleryImagePageViewModel), typeof(AudioGalleryPageViewModel), typeof(VideoGalleryPageViewModel),
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
        services.TryAddScoped(provider =>
        {
            var navigation = provider.GetRequiredService<IGameNavigationService>();
            return new GameLaunchFlow(
                token => navigation.ResetToAsync<LoadingPageViewModel>(cancellationToken: token),
                token => navigation.ResetToAsync<GamePageViewModel>(cancellationToken: token),
                token => navigation.ResetToAsync<TitlePageViewModel>(cancellationToken: token));
        });
        services.TryAddScoped<IPageViewFactory, PageViewFactory>();
        services.TryAddScoped<IGameScreenshotService, AvaloniaGameScreenshotService>();
        services.TryAddScoped<GalleryMediaService>();
        services.TryAddScoped<GameShellViewModel>();
        services.TryAddScoped<TitlePageViewModel>();
        services.TryAddScoped<GamePageViewModel>();
        services.TryAddScoped<GalNet.Avalonia.GameView.Presentation.GamePresentationState>(provider => provider.GetRequiredService<GamePageViewModel>());
        services.TryAddScoped<SaveSlotsPageViewModel>();
        services.TryAddScoped<SettingsPageViewModel>();
        services.TryAddScoped<GalleryPageViewModel>();
        services.TryAddScoped<CgGalleryImagePageViewModel>();
        services.TryAddScoped<VideoGalleryPlayerPageViewModel>();
        services.TryAddScoped<SceneGalleryPlayerPageViewModel>();
        services.TryAddScoped<MissingGalleryPageViewModel>();
        services.TryAddScoped<AboutPageViewModel>();
        services.TryAddScoped<LoadingPageViewModel>();
        services.TryAddScoped(provider => ActivatorUtilities.CreateInstance<GameShell>(provider,
            new Action(() => provider.GetRequiredService<IGameNavigationService>().ResetTo<TitlePageViewModel>()),
            new Func<string, Task>(action =>
            {
                if (provider.GetRequiredService<IGameNavigationService>().CurrentViewModel is not GamePageViewModel game)
                    return Task.CompletedTask;
                switch (action)
                {
                    case "open.save": return game.OpenSaveSlotsCommand.ExecuteAsync(null);
                    case "open.load": return game.OpenLoadSlotsCommand.ExecuteAsync(null);
                    case "open.settings": game.OpenSettingsCommand.Execute(null); break;
                }
                return Task.CompletedTask;
            })));

        if (gamePageType == typeof(GamePage)) services.TryAddScoped<GamePage>();
        else services.TryAddScoped<GamePage>(provider => (GamePage)provider.GetRequiredService(gamePageType));
        return services;
    }
}
