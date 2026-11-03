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
    /// <summary>Ordered factories create fresh pages for each application startup.</summary>
    public static Opening.OpeningSequence CreateOpeningSequence(IPlayerLocalization text) => new(
        [() => new Opening.EngineLogoPageViewModel(),
         () => new Opening.DisclaimerPageViewModel(text)]);

    public static GamePalette PlayerPalette { get; } = GamePalette.Mono;
    public const int PageSize = 12;
    public static TimeSpan MinimumLoadingDwell { get; } = TimeSpan.FromMilliseconds(350);
    // The project toast spends 0.8 seconds entering, then leaves two seconds to read.
    public static TimeSpan AudioAnnouncementDuration { get; } = TimeSpan.FromSeconds(2.8);
    public static global::Avalonia.Styling.IStyle CreateStyles(GamePalette? palette = null)
    {
        palette ??= PlayerPalette;
        var fluent = new global::Avalonia.Themes.Fluent.FluentTheme();
        fluent.Palettes[global::Avalonia.Styling.ThemeVariant.Light] = palette.CreateFluentPalette();
        fluent.Palettes[global::Avalonia.Styling.ThemeVariant.Dark] = palette.CreateFluentPalette();
        var origin = new Uri("avares://GalNet.Sample.CrossPlatform/");
        var styles = new global::Avalonia.Styling.Styles
        {
            fluent,
            new global::Avalonia.Markup.Xaml.Styling.StyleInclude(origin) { Source = new Uri("avares://GalNet.Avalonia.GameView/Styles/Controls.axaml") },
            new global::Avalonia.Markup.Xaml.Styling.StyleInclude(origin) { Source = new Uri(origin, "Styles/Player.axaml") }
        };
        styles.Resources["GalNet.Theme.FontFamily"] = GameTypography.UiFontFamily;
        styles.Resources.MergedDictionaries.Add(palette.CreateResources());
        styles.Resources.MergedDictionaries.Add(new global::Avalonia.Markup.Xaml.Styling.ResourceInclude(origin) { Source = new Uri(origin, "Styles/Dimensions.axaml") });
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
            styles.Resources["GalNet.Theme.ControlMinHeight"] = 44d;
        PlayerTypography.Apply(styles.Resources, PlayerTypography.Default);
        return styles;
    }

    public static GalNet.Sample.CrossPlatform.Views.PlayerView CreateView()
    {
        var view = new GalNet.Sample.CrossPlatform.Views.PlayerView();
        ConfigureView(view);
        return view;
    }

    public static void ConfigureView(GalNet.Sample.CrossPlatform.Views.PlayerView view)
    {
        view.AudioAnnouncementDuration = AudioAnnouncementDuration;
        view.Resources.MergedDictionaries.Add(PlayerPalette.CreateResources());
        view.Styles.Add(CreateStyles());
    }

    public static void ConfigureServices(IServiceCollection services, Action<IPageViewRegistryBuilder>? configureViews = null)
    {
        ConfigurePages(services);
        services.AddAvaloniaGameView(views =>
            {
                views.Register<ViewModels.TitlePageViewModel, Views.TitlePage>();
                views.Register<ViewModels.LoadingPageViewModel, Views.LoadingPage>();
                views.Register<ViewModels.GamePageViewModel, Views.GamePage>();
                views.Register<ViewModels.SettingsPageViewModel, Views.SettingsPage>();
                views.Register<ViewModels.SaveSlotsPageViewModel, Views.SaveSlotsPage>();
                views.Register<ViewModels.AboutPageViewModel, Views.AboutPage>();
                views.Register<ViewModels.GalleryPageViewModel, Views.GalleryPage>();
                views.Register<ViewModels.MissingGalleryPageViewModel, Views.MissingGalleryPage>();
                views.Register<ViewModels.CgGalleryImagePageViewModel, Views.CgGalleryImagePage>();
                views.Register<ViewModels.VideoGalleryPlayerPageViewModel, Views.VideoGalleryPlayerPage>();
                views.Register<ViewModels.SceneGalleryPlayerPageViewModel, Views.SceneGalleryPlayerPage>();
                views.Register<ScreenshotDialogViewModel, Views.ScreenshotDialog>();
                configureViews?.Invoke(views);
            }, galleries =>
            {
                galleries.Add<ViewModels.CgGalleryPageViewModel, Views.CgGalleryPage>("cg");
                galleries.Add<ViewModels.AudioGalleryPageViewModel, Views.AudioGalleryPage>("audio");
                galleries.Add<ViewModels.VideoGalleryPageViewModel, Views.VideoGalleryPage>("video");
                galleries.Add<ViewModels.SceneGalleryPageViewModel, Views.SceneGalleryPage>("scene");
            })
            .AddSingleton(_ => CreateLocalizationService())
            .AddSingleton<IPlayerLocalization>(provider => provider.GetRequiredService<AvaloniaPlayerLocalization>())
            .AddSingleton<GalNet.Core.Services.ITextResolver>(provider => provider.GetRequiredService<IPlayerLocalization>())
            .AddSingleton<LibVlcRuntime>()
            .AddScoped(provider => new AvaloniaPlayerSettingsService(provider.GetRequiredService<IPlayerLocalization>(),
                provider.GetRequiredService<GamePresentationState>(), editors: CreateFieldEditors(), defaultFontSize: PlayerTypography.Default))
            .AddScoped<IPlayerStartup, AvaloniaPlayerStartup>()
            .AddScoped<LanguageSelectionViewModel>()
            .AddScoped(_ => CreateView())
            .AddScoped<IAvaloniaPlayerSettings>(provider => provider.GetRequiredService<AvaloniaPlayerSettingsService>())
            .AddScoped<IPlayerSettingsService>(provider => provider.GetRequiredService<IAvaloniaPlayerSettings>());
    }

    private static void ConfigurePages(IServiceCollection services)
    {
        services.AddScoped<GamePresentationState>();
        services.AddScoped(provider => new GalleryNavigationService(provider,
            provider.GetRequiredService<IGameNavigationService>(), provider.GetRequiredService<IGalleryPageRegistry>(),
            (gallery, token) => provider.GetRequiredService<IGameNavigationService>()
                .NavigateAsync<ViewModels.MissingGalleryPageViewModel, GalleryTypeData>(gallery, token)));
        services.AddScoped(provider =>
        {
            var state = new GamePresentationState();
            var model = new ViewModels.GamePageViewModel(state, provider.GetRequiredService<IGameNavigationService>());
            var surface = CreateGameView(provider, model, scoped: false);
            return new GalleryScenePlayback(provider.GetRequiredService<IGameSessionService>(), surface);
        });
        services.AddScoped<GamePage>(provider =>
        {
            var model = provider.GetRequiredService<ViewModels.GamePageViewModel>();
            return CreateGameView(provider, model, scoped: true);
        });
        services.AddScoped<IPlayerApplication, PlayerApplication>();
        services.AddScoped(provider =>
        {
            var navigation = provider.GetRequiredService<IGameNavigationService>();
            return new GameLaunchFlow(
                async token =>
                {
                    ArgumentOutOfRangeException.ThrowIfLessThan(MinimumLoadingDwell, TimeSpan.Zero);
                    await navigation.ResetToAsync<ViewModels.LoadingPageViewModel>(cancellationToken: token);
                    await Task.Delay(MinimumLoadingDwell, token);
                },
                token => navigation.ResetToAsync<ViewModels.GamePageViewModel>(cancellationToken: token),
                token => navigation.ResetToAsync<ViewModels.TitlePageViewModel>(cancellationToken: token));
        });
        services.AddScoped(provider =>
        {
            var shell = ActivatorUtilities.CreateInstance<GameShell>(provider,
                new Action(() => provider.GetRequiredService<IGameNavigationService>().ResetTo<ViewModels.TitlePageViewModel>()),
                new Func<string, Task>(action => provider.GetRequiredService<ViewModels.GamePageViewModel>().HandleInputActionAsync(action)));
            shell.Styles.Add(CreateStyles());
            return shell;
        });
    }

    private static GamePage CreateGameView(IServiceProvider services, ViewModels.GamePageViewModel model, bool scoped)
    {
        var type = services.GetRequiredService<IPageViewRegistry>().GetViewType(typeof(ViewModels.GamePageViewModel));
        var page = (GamePage)(scoped && type != typeof(GamePage) ? services.GetRequiredService<IPageViewFactory>().Create(model)
            : ActivatorUtilities.CreateInstance(new ReplayViewServices(services, model), type));
        page.Presentation = model.State;
        page.DataContext = model;
        return page;
    }

    // Borrow other scoped services, but never inject the player's state or VM into a replay view.
    private sealed class ReplayViewServices(IServiceProvider services, ViewModels.GamePageViewModel model) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(GamePresentationState) ? model.State
            : type == typeof(ViewModels.GamePageViewModel) ? model
            : type == typeof(IServiceProvider) ? this : services.GetService(type);
    }

    public static EditorRegistry CreateFieldEditors()
    {
        var editors = EditorRegistry.CreateDefault();
        static FieldEditorLease LimitWidth(FieldEditorLease editor, double width)
        {
            editor.Control.MaxWidth = width;
            editor.Control.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left;
            return editor;
        }
        FieldEditorLease Numeric(FieldEditorContext context)
        {
            var editor = LimitWidth(DefaultFieldEditors.CreateNumeric(context), 160);
            editor.Control.Classes.Add("player-number");
            editor.Control.Width = 160;
            return editor;
        }
        editors.Replace(GalNet.Core.Parameters.ValueEditorIds.Numeric, Numeric);
        editors.Replace(GalNet.Core.Parameters.ValueEditorIds.NumericText, Numeric);
        editors.Replace(GalNet.Core.Parameters.ValueEditorIds.Choice, context =>
        {
            var editor = LimitWidth(DefaultFieldEditors.CreateChoice(context), 360);
            editor.Control.MinWidth = 280;
            return editor;
        });
        editors.Replace(GalNet.Core.Parameters.ValueEditorIds.Slider, context =>
        {
            var editor = LimitWidth(DefaultFieldEditors.CreateSlider(context), 520);
            editor.Control.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
            return editor;
        });
        return editors.Freeze();
    }

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
            LaunchPolicy = GalNet.Storage.FileSystem.GameLaunchPolicy.FromAssembly(typeof(SampleCustomization).Assembly),
            NoGameMessage = "Usage: GalNet.Sample.CrossPlatform.Desktop <game-data-directory> [--profile <directory>]",
            CreateContentCatalogs = CreateContentCatalogs,
            AttachRun = attachRun,
            Content = new GameContentLoadOptions
            {
                Configurations = ProjectSchemas.Configurations,
                SelectDefaultCulture = configuration => configuration.ReadDefaultOptions().TargetLocale.Code
            },
            ApplyProjectOrientationAsync = App.ApplyProjectOrientationAsync,
            SelectProjectOptions = configuration =>
            {
                var options = configuration.ReadDefaultOptions();
                return new()
                {
                    SaveSlotCount = options.SaveSlotCount,
                    DefaultCulture = options.TargetLocale.Code,
                    Audio = new(options.AudioTracks, options.AudioBuses),
                    OrientationPolicy = options.OrientationPolicy,
                    DefaultOrientation = options.DefaultOrientation
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
