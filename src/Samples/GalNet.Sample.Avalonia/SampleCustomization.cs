using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.Composition;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.Themes;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Assets;
using GalNet.Game.Controls.Forms;
using GalNet.GameHost.Content;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Project.Contracts;
using GalNet.Media.LibVlc;
using GalNet.Avalonia.GameHost.ViewModels;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Sample.Avalonia.Presentation;
using GalNet.Sample.Avalonia.Services;
using GalNet.Sample.Avalonia.Views;
using Microsoft.Extensions.DependencyInjection;
#if GALNET_GAME_DEBUG
using GalNet.Sample.Avalonia.Debug;
#endif

namespace GalNet.Sample.Avalonia;

/// <summary>Project choices and desktop services owned by this Sample.</summary>
internal static class SampleCustomization
{
    internal static GamePalette PlayerPalette { get; } = GamePalette.Mono;
    internal static IGameLayoutTheme PlayerTheme { get; } = new ClassicTheme(PlayerPalette);

    internal static void ConfigureView(GalNet.Avalonia.GameHost.Views.PlayerView view)
    {
        view.Resources.MergedDictionaries.Add(PlayerPalette.CreateResources());
        view.Styles.Add(PlayerTheme.CreateStyles());
    }

    internal static void ConfigureServices(IServiceCollection services, bool enableDebug = true, Action<IPageViewRegistryBuilder>? configureViews = null)
    {
        services.AddAvaloniaGameViewPages(theme: PlayerTheme, configureViews: configureViews)
            .AddSingleton(_ => CreateLocalizationService())
            .AddSingleton<IPlayerLocalization>(provider => provider.GetRequiredService<AvaloniaPlayerLocalization>())
            .AddSingleton<LibVlcRuntime>()
            .AddScoped(provider => new AvaloniaPlayerSettingsService(provider.GetRequiredService<IPlayerLocalization>(),
                provider.GetRequiredService<GamePageViewModel>(), editors: CreateFieldEditors()))
            .AddScoped<IPlayerStartup, AvaloniaPlayerStartup>()
            .AddScoped<LanguageSelectionViewModel>()
            .AddScoped<IAvaloniaPlayerSettings>(provider => provider.GetRequiredService<AvaloniaPlayerSettingsService>())
            .AddScoped<IPlayerSettingsService>(provider => provider.GetRequiredService<IAvaloniaPlayerSettings>());
        services.AddScoped<SampleGameSessionService>()
            .AddScoped<IGameSessionService>(provider => provider.GetRequiredService<SampleGameSessionService>())
            .AddTransient<MainWindow>();
#if GALNET_GAME_DEBUG
        if (enableDebug) services.AddSampleDebug();
        if (enableDebug) services.AddScoped<SampleExternalDebug>();
#endif
    }

    internal static EditorRegistry CreateFieldEditors() => EditorRegistry.CreateDefault().Freeze();
    internal static AvaloniaPlayerLocalization CreateLocalizationService() =>
        AvaloniaPlayerLocalization.FromEmbeddedResources([typeof(SampleCustomization).Assembly]);

    internal static string GetProfileDirectory(string content, string? profileRoot = null) =>
        GalNet.Storage.FileSystem.GameProfilePaths.GetProfileDirectory(content,
            profileRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GalNet"));

    internal static (IResourceTypeCatalog Resources, IGalleryTypeCatalog Gallery) CreateContentCatalogs()
    {
        var resources = BuiltinResourceTypes.CreateCatalog();
        return (resources, BuiltinGalleryTypes.CreateCatalog(resources));
    }

    internal static AvaloniaGameHostOptions CreateHostOptions(Func<GalNet.Runtime.Engine.GameEngine, IDisposable>? attachRun = null) => new()
    {
        GameTitle = "GalNet Avalonia Sample",
        NoGameMessage = "Usage: GalNet.Sample.Avalonia <game-data-directory> [--profile <directory>]",
        CreateContentCatalogs = CreateContentCatalogs,
        AttachRun = attachRun,
        Content = new GameContentLoadOptions
        {
            Configurations = ProjectSchemas.Configurations,
            SelectDefaultCulture = configuration => configuration.ReadDefaultOptions().TargetLocale.Code
        },
        SelectProjectOptions = configuration =>
        {
            var options = configuration.ReadDefaultOptions();
            return new()
            {
                SaveSlotCount = options.SaveSlotCount,
                DefaultCulture = options.TargetLocale.Code,
                Audio = new(options.AudioTracks, options.AudioBuses)
            };
        },
        ConfigureExecution = (registration, presentation, page, content) => ConfigureExecution(registration,
            presentation.Presenters, page, content, presentation.Effects, presentation.Audio,
            presentation.Video, presentation.AudioModule, presentation.ImageVariants)
    };

    internal static void ConfigureExecution(GameRegistration registration, AvaloniaGamePresenters presenters, GamePage page,
        GameContent content, AvaloniaEffectRuntime? effects, IAudioPlaybackHost? audio, IVideoPresenter? video,
        BuiltinAudioModule? audioModule, GalNet.Contracts.Builtins.Variants.ImageVariantsCatalog? imageVariants = null)
    {
        registration.UseCatalog(ProjectSchemas.CreateCatalog());
        registration.AddBuiltinImplementations(registration.BuildCatalog(),
            presenters.DialoguePresenter, presenters.LayerPresenter, presenters.AnimationPresenter,
            effects, content.Gallery, presenters.ParticlePresenter,
            presenters.NvlPresenter, audio, video, content.Localization.Characters, audioModule, imageVariants);
        registration.RegisterImplementation(new SampleScreenshotModule(page,
            () => SampleScreenshotOutput.GetHostDirectory(content.Graph.Name)));
    }
}
