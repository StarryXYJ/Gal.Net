using DynamicLocalization.Core;
using DynamicLocalization.Core.Providers;
using GalNet.Assets;
using GalNet.Avalonia.GameView.Composition;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.Themes;
using GalNet.Core.Assets;
using GalNet.Core.Gallery;
using GalNet.Core.Scene;
using GalNet.Game.Controls.Forms;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Project.Contracts;
using GalNet.Rendering.Scene;
using GalNet.Runtime.Content;
using GalNet.Sample.Avalonia.Debug;
using GalNet.Sample.Avalonia.Presentation;
using GalNet.Sample.Avalonia.Services;
using GalNet.Sample.Avalonia.ViewModels;
using GalNet.Sample.Avalonia.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Sample.Avalonia;

/// <summary>Project choices. The host owns services and resources; modules borrow platform ports.</summary>
internal static class SampleCustomization
{
    internal static IGameLayoutTheme PlayerTheme { get; } = new ClassicTheme(GamePalette.Mono);

    internal static void ConfigureServices(IServiceCollection services, bool enableDebug = true)
    {
        services.AddAvaloniaGameViewPages(theme: PlayerTheme)
            .AddSingleton(_ => CreateLocalizationService())
            .AddSingleton<SampleLibVlcRuntime>()
            .AddScoped<SamplePlayerSettingsService>()
            .AddScoped<SampleStartupService>()
            .AddScoped<LanguageSelectionViewModel>()
            .AddScoped<IPlayerSettingsService>(provider => provider.GetRequiredService<SamplePlayerSettingsService>())
            .AddScoped<SampleGameSessionService>()
            .AddScoped<IGameSessionService>(provider => provider.GetRequiredService<SampleGameSessionService>())
            .AddTransient<MainWindow>();
        if (enableDebug) services.AddSampleDebug();
    }

    internal static EditorRegistry CreateFieldEditors() => EditorRegistry.CreateDefault();

    internal static SampleLocalizationService CreateLocalizationService()
    {
        var cultureService = new CultureService();
        var provider = new JsonLocalizationProvider();
        provider.Initialize(new JsonLocalizationProviderOptions
        {
            BasePath = "Localization",
            UseEmbeddedResources = true,
            Assembly = typeof(SampleCustomization).Assembly
        });
        cultureService.RegisterProvider(provider);
        cultureService.SetCulture("en-US");
        LocalizationService.Initialize(cultureService);
        return SampleLocalizationService.Create(cultureService, [provider]);
    }

    internal static (IResourceTypeCatalog Resources, IGalleryTypeCatalog Gallery) CreateContentCatalogs()
    {
        var resources = BuiltinResourceTypes.CreateCatalog();
        return (resources, BuiltinGalleryTypes.CreateCatalog(resources));
    }

    internal static void ConfigureDecoders(IAssetManager assets) =>
        assets.RegisterDecoder<SceneTexture>("sprite", new SceneTextureAssetDecoder());

    internal static Task<IReadOnlyList<IGameFile>> SelectTexturePreloadsAsync(IAssetManager assets, CancellationToken token) =>
        assets.GetFilesAsync("sprite", token);

    internal static async Task<EffectProgramResource[]> SelectEffectProgramsAsync(IAssetManager assets, CancellationToken token) =>
        (await assets.GetFilesAsync("effect-program", token)).Select(file => new EffectProgramResource(file.Id)).ToArray();

    internal static void ConfigureExecution(
        GameRegistration registration, AvaloniaGamePresenters presenters, GamePage page,
        GameContent content, AvaloniaEffectRuntime? effects, AvaloniaAudioPlaybackHost? audio,
        AvaloniaVideoPresenter? video, BuiltinAudioModule? audioModule)
    {
        registration.UseCatalog(ProjectSchemas.CreateCatalog());
        registration.AddBuiltinImplementations(registration.BuildCatalog(),
            presenters.DialoguePresenter, presenters.LayerPresenter, presenters.AnimationPresenter,
            effects, content.Gallery, presenters.ParticlePresenter,
            presenters.NvlPresenter, audio, video, content.Localization.Characters, audioModule);
        registration.RegisterImplementation(new SampleScreenshotModule(page,
            () => SampleScreenshotOutput.GetHostDirectory(content.Graph.Name)));
    }
}
