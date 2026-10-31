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

}
