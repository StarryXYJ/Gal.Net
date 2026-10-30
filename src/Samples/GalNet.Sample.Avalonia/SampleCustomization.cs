using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.Composition;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.Themes;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Assets;
using GalNet.Game.Controls.Forms;
using GalNet.GameHost.Content;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Sample.Avalonia.Services;
using GalNet.Sample.Avalonia.Views;
using GalNet.Sample.Player;
using Microsoft.Extensions.DependencyInjection;
#if GALNET_GAME_DEBUG
using GalNet.Sample.Avalonia.Debug;
#endif

namespace GalNet.Sample.Avalonia;

/// <summary>Desktop entry, developer tools and project customization of the shared Sample player.</summary>
internal static class SampleCustomization
{
    internal static GamePalette PlayerPalette => SamplePlayerServices.PlayerPalette;
    internal static IGameLayoutTheme PlayerTheme => SamplePlayerServices.PlayerTheme;

    internal static void ConfigureServices(IServiceCollection services, bool enableDebug = true, Action<IPageViewRegistryBuilder>? configureViews = null)
    {
        SamplePlayerServices.ConfigureServices(services, configureViews);
        services.AddScoped<SampleGameSessionService>()
            .AddScoped<IGameSessionService>(provider => provider.GetRequiredService<SampleGameSessionService>())
            .AddTransient<MainWindow>();
#if GALNET_GAME_DEBUG
        if (enableDebug) services.AddSampleDebug();
        if (enableDebug) services.AddScoped<SampleExternalDebug>();
#endif
    }

    internal static EditorRegistry CreateFieldEditors() => SamplePlayerServices.CreateFieldEditors();
    internal static AvaloniaPlayerLocalization CreateLocalizationService() => SamplePlayerServices.CreateLocalizationService();
    internal static (IResourceTypeCatalog Resources, IGalleryTypeCatalog Gallery) CreateContentCatalogs() => SamplePlayerServices.CreateContentCatalogs();
    internal static AvaloniaGameHostOptions CreateHostOptions(Func<GalNet.Runtime.Engine.GameEngine, IDisposable>? attachRun = null) =>
        SamplePlayerServices.CreateHostOptions(attachRun);

    internal static void ConfigureExecution(GameRegistration registration, AvaloniaGamePresenters presenters, GamePage page,
        GameContent content, AvaloniaEffectRuntime? effects, IAudioPlaybackHost? audio, IVideoPresenter? video,
        BuiltinAudioModule? audioModule, GalNet.Contracts.Builtins.Variants.ImageVariantsCatalog? imageVariants = null) =>
        SamplePlayerServices.ConfigureExecution(registration, presenters, page, content, effects, audio, video, audioModule, imageVariants);
}
