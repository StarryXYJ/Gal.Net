using GalNet.Contracts.Builtins.Assets;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.Composition;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Avalonia.Themes;
using GalNet.Core.Assets;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Contracts.Builtins;
using GalNet.Game.Controls.Forms;
using GalNet.Media.LibVlc;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Project.Contracts;
using GalNet.GameHost.Content;
using GalNet.Sample.CrossPlatform.Presentation;
using GalNet.Avalonia.GameHost.ViewModels;
using GalNet.Presentation.Abstractions.Application;
using Microsoft.Extensions.DependencyInjection;
#if GALNET_GAME_DEBUG
using GalNet.Avalonia.Debugging;
using GalNet.Runtime.Engine;
#endif

namespace GalNet.Sample.CrossPlatform;

/// <summary>Project choices. The host owns services and resources; modules borrow platform ports.</summary>
public static class SampleCustomization
{
    public static GamePalette PlayerPalette { get; } = GamePalette.Mono;
    public static IGameLayoutTheme PlayerTheme { get; } = new ClassicTheme(PlayerPalette);

    public static GalNet.Avalonia.GameHost.Views.PlayerView CreateView()
    {
        var view = new GalNet.Avalonia.GameHost.Views.PlayerView();
        ConfigureView(view);
        return view;
    }

    public static void ConfigureView(GalNet.Avalonia.GameHost.Views.PlayerView view)
    {
        view.Resources.MergedDictionaries.Add(PlayerPalette.CreateResources());
        view.Styles.Add(PlayerTheme.CreateStyles());
    }

    public static void ConfigureServices(IServiceCollection services, Action<IPageViewRegistryBuilder>? configureViews = null)
    {
        services.AddAvaloniaGameViewPages(theme: PlayerTheme, configureViews: configureViews)
            .AddSingleton(_ => CreateLocalizationService())
            .AddSingleton<IPlayerLocalization>(provider => provider.GetRequiredService<AvaloniaPlayerLocalization>())
            .AddSingleton<LibVlcRuntime>()
            .AddScoped(provider => new AvaloniaPlayerSettingsService(provider.GetRequiredService<IPlayerLocalization>(),
                provider.GetRequiredService<GamePageViewModel>(), editors: CreateFieldEditors()))
            .AddScoped<IPlayerStartup, AvaloniaPlayerStartup>()
            .AddScoped<LanguageSelectionViewModel>()
            .AddScoped(_ => CreateView())
            .AddScoped<IAvaloniaPlayerSettings>(provider => provider.GetRequiredService<AvaloniaPlayerSettingsService>())
            .AddScoped<IPlayerSettingsService>(provider => provider.GetRequiredService<IAvaloniaPlayerSettings>());
    }

    public static EditorRegistry CreateFieldEditors() => EditorRegistry.CreateDefault().Freeze();

    public static AvaloniaPlayerLocalization CreateLocalizationService() =>
#if GALNET_GAME_DEBUG
        AvaloniaPlayerLocalization.FromEmbeddedResources([typeof(GameDebugViewModel).Assembly, typeof(SampleCustomization).Assembly]);
#else
        AvaloniaPlayerLocalization.FromEmbeddedResources([typeof(SampleCustomization).Assembly]);
#endif

#if GALNET_GAME_DEBUG
    public static GameDebugHostOptions CreateDebugOptions(Func<CancellationToken, Task> restartAsync, Action? openLogDirectory = null)
    {
        var instructions = GameDebugHostOptions.CreateBuiltinReplayPolicy();
        instructions[ScreenshotSchema.TypeId] = DebugReplayBehavior.Ignore;
        return new() { RestartAsync = restartAsync, OpenLogDirectory = openLogDirectory, Instructions = instructions };
    }
#endif

    public static string GetProfileDirectory(string content, string? profileRoot = null) =>
        GalNet.Storage.FileSystem.GameProfilePaths.GetProfileDirectory(content,
            profileRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GalNet"));

    public static (IResourceTypeCatalog Resources, IGalleryTypeCatalog Gallery) CreateContentCatalogs()
    {
        var resources = BuiltinResourceTypes.CreateCatalog();
        return (resources, BuiltinGalleryTypes.CreateCatalog(resources));
    }

    public static AvaloniaGameHostOptions CreateHostOptions(Func<GalNet.Runtime.Engine.GameEngine, IDisposable>? attachRun = null,
        Func<string, string>? screenshotDirectory = null) => new()
    {
        GameTitle = "GalNet CrossPlatform Sample",
        NoGameMessage = "Usage: GalNet.Sample.CrossPlatform.Desktop <game-data-directory> [--profile <directory>]",
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
            presentation.Video, presentation.AudioModule, presentation.ImageVariants, screenshotDirectory)
    };

    public static void ConfigureExecution(
        GameRegistration registration, AvaloniaGamePresenters presenters, GamePage page,
        GameContent content, AvaloniaEffectRuntime? effects, IAudioPlaybackHost? audio,
        IVideoPresenter? video, BuiltinAudioModule? audioModule, GalNet.Contracts.Builtins.Variants.ImageVariantsCatalog? imageVariants = null,
        Func<string, string>? screenshotDirectory = null)
    {
        registration.UseCatalog(ProjectSchemas.CreateCatalog());
        registration.AddBuiltinImplementations(registration.BuildCatalog(),
            presenters.DialoguePresenter, presenters.LayerPresenter, presenters.AnimationPresenter,
            effects, content.Gallery, presenters.ParticlePresenter,
            presenters.NvlPresenter, audio, video, content.Localization.Characters, audioModule, imageVariants);
        registration.RegisterImplementation(new SampleScreenshotModule(page,
            () => (screenshotDirectory ?? SampleScreenshotOutput.GetHostDirectory)(content.Graph.Name)));
    }
}
