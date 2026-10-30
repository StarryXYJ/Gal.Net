using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using DynamicLocalization.Core;
using DynamicLocalization.Core.Providers;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameHost.Presentation;
using GalNet.Avalonia.GameHost.ViewModels;
using GalNet.Avalonia.GameHost.Views;
using GalNet.Avalonia.GameView.Composition;
using GalNet.Avalonia.GameView.MarkupExtensions;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Avalonia.Themes;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Media.LibVlc;
using GalNet.Presentation.Abstractions.Application;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Presentation;

[NonParallelizable]
public sealed class AvaloniaPlayerHostTests
{
    private string _root = null!;

    [SetUp]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "galnet-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "content", "Graph"));
        File.WriteAllText(Path.Combine(_root, "content", "Graph", "graph.json"),
            """{"version":2,"name":"Independent","rootNodeId":"","nodes":[],"edges":[],"playerVariables":[],"saveVariables":[]}""");
    }

    [TearDown]
    public void Cleanup() => Directory.Delete(_root, true);

    internal static AvaloniaGameHostOptions Options(int saveSlots = 60,
        AvaloniaGamePresentationOptions? presentation = null,
        Func<GalNet.Runtime.Engine.GameEngine, IDisposable>? attachRun = null) => new()
    {
        SelectProjectOptions = _ => new() { SaveSlotCount = saveSlots },
        Presentation = presentation ?? new(),
        AttachRun = attachRun,
        CreateContentCatalogs = () =>
        {
            var resources = BuiltinResourceTypes.CreateCatalog();
            return (resources, BuiltinGalleryTypes.CreateCatalog(resources));
        },
        ConfigureExecution = (_, _, _, _) => { }
    };

    internal static void Configure(IServiceCollection services, string label)
    {
        var theme = new ClassicTheme(GamePalette.Mono);
        services.AddAvaloniaGameViewPages(theme)
            .AddSingleton<IPlayerLocalization>(_ =>
            {
                var cultures = new CultureService();
                var provider = new HostText(label);
                cultures.RegisterProvider(provider);
                cultures.SetCulture("en-US");
                return new AvaloniaPlayerLocalization(cultures, [provider]);
            })
            .AddSingleton<LibVlcRuntime>()
            .AddScoped<IPlayerStartup, AvaloniaPlayerStartup>()
            .AddScoped<IAvaloniaPlayerSettings>(provider => new AvaloniaPlayerSettingsService(
                provider.GetRequiredService<IPlayerLocalization>(), provider.GetRequiredService<GamePageViewModel>()))
            .AddScoped<IPlayerSettingsService>(provider => provider.GetRequiredService<IAvaloniaPlayerSettings>())
            .AddScoped(_ =>
            {
                var view = new PlayerView();
                view.Resources.MergedDictionaries.Add(GamePalette.Mono.CreateResources());
                view.Resources["HostLabel"] = label;
                view.Styles.Add(theme.CreateStyles());
                return view;
            });
    }

    private static Task Ui(Func<Task> action) =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly)
            .Dispatch(async () => { await action(); return true; }, TestContext.CurrentContext.CancellationToken);

    [Test]
    public void ProjectResourceFallbackDoesNotHideExternalDefaultCultureOverrides()
    {
        using var localization = AvaloniaPlayerLocalization.FromEmbeddedResources([typeof(AvaloniaPlayerHostTests).Assembly]);
        Assert.That(localization["App.Title"], Is.EqualTo("Independent host"));
        Assert.That(localization["Settings.Title"], Is.EqualTo("Settings"));
        localization.AttachGame(new GalNet.GameHost.Content.GameContent
        {
            Graph = new(),
            Localization = new()
            {
                ExternalSources = [new("en-US", "override", """{"Host.OnlyEnglish":"External English"}""")]
            }
        });
        localization.ApplyCulture("zh-CN");
        Assert.That(localization["Settings.Title"], Is.EqualTo("设置"));
        Assert.That(localization["Host.OnlyEnglish"], Is.EqualTo("External English"),
            "Default-culture fallback must apply after every source has checked the selected culture.");
    }

    [Test]
    public void CommonUiResourcesContainNoSampleIdentityOrDebugText()
    {
        using var localization = AvaloniaPlayerLocalization.FromEmbeddedResources([]);
        Assert.That(localization["Onboarding.Title"], Is.EqualTo("Choose your language"));
        Assert.That(localization["Settings.Title"], Is.EqualTo("Settings"));
        Assert.That(localization["App.Title"], Is.EqualTo("#App.Title#"));
        Assert.That(localization["Debug.Menu"], Is.EqualTo("#Debug.Menu#"));
        localization.AttachGame(new GalNet.GameHost.Content.GameContent { Graph = new() });
        localization.ApplyCulture("zh-CN");
        Assert.That(localization["Settings.Title"], Is.EqualTo("设置"));
        Assert.That(localization["App.Title"], Is.EqualTo("#App.Title#"));
    }

    [Test]
    public Task IndependentHostCanInitializeAndReopenWithItsOwnConfiguration() => Ui(async () =>
    {
        var previous = PlayerTextExtension.Resolve;
        var profile = Path.Combine(_root, "profile");
        foreach (var label in new[] { "First", "Second" })
        {
            var host = await AvaloniaPlayerHost.CreateAsync(Options(), services => Configure(services, label));
            var window = new Window { Content = host.View, Width = 800, Height = 500 };
            window.Show();
            try
            {
                Assert.That(host.View.Resources["HostLabel"], Is.EqualTo(label));
                Assert.That(host.Services.GetRequiredService<IPlayerLocalization>().CultureService["Onboarding.Title"], Is.EqualTo(label));
                var startup = host.InitializeAsync(Path.Combine(_root, "content"), profile);
                var language = host.Services.GetRequiredService<LanguageSelectionViewModel>();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                while (!startup.IsCompleted)
                {
                    if (language.ConfirmCommand.CanExecute(null)) language.ConfirmCommand.Execute(null);
                    await Task.Delay(10, deadline.Token);
                }
                Assert.That(await startup, Is.True);
                host.Services.GetRequiredService<TitlePageViewModel>().CanExit = false;
                Assert.That(await host.HandleBackAsync(), Is.True);
                await host.SuspendAsync();
                await host.SuspendAsync();
                Assert.That(host.Services.GetRequiredService<IGameNavigationService>().CurrentViewModel, Is.TypeOf<TitlePageViewModel>());
            }
            finally
            {
                window.Close();
                await host.DisposeAsync();
            }
            Assert.That(PlayerTextExtension.Resolve, Is.SameAs(previous));
        }
    });

    [Test]
    public Task ReleasingTheHostRestoresAnExistingLocalizationResolver() => Ui(async () =>
    {
        var original = PlayerTextExtension.Resolve;
        Func<string, object> existing = key => "Existing:" + key;
        PlayerTextExtension.Resolve = existing;
        try
        {
            await using (var host = await AvaloniaPlayerHost.CreateAsync(Options(), services => Configure(services, "Owned")))
                Assert.That(PlayerTextExtension.Resolve, Is.Not.SameAs(existing));
            Assert.That(PlayerTextExtension.Resolve, Is.SameAs(existing));
        }
        finally { PlayerTextExtension.Resolve = original; }
    });

    [Test]
    public Task InvalidContentShowsTheStartupFailureAndDisposesNormally() => Ui(async () =>
    {
        var previous = PlayerTextExtension.Resolve;
        var host = await AvaloniaPlayerHost.CreateAsync(Options(), services => Configure(services, "Failure"));
        try
        {
            Assert.That(await host.InitializeAsync(Path.Combine(_root, "missing"), Path.Combine(_root, "profile")), Is.False);
            var status = host.View.FindControl<TextBlock>("StartupStatus")!;
            Assert.That(status.IsVisible, Is.True);
            Assert.That(status.Text, Is.EqualTo(host.Session.StatusMessage));
            Assert.That(host.View.FindControl<GalNet.Game.Controls.GameCanvasHost>("GameCanvas")!.IsVisible, Is.False);
        }
        finally { await host.DisposeAsync(); }
        Assert.That(PlayerTextExtension.Resolve, Is.SameAs(previous));
    });

    [Test]
    public Task DisposalCancelsLanguageSelectionAndPreservesThePreviousResolver() => Ui(async () =>
    {
        var previous = PlayerTextExtension.Resolve;
        var host = await AvaloniaPlayerHost.CreateAsync(Options(), services => Configure(services, "Cancel"));
        try
        {
            var startup = host.InitializeAsync(Path.Combine(_root, "content"), Path.Combine(_root, "cancel-profile"));
            var language = host.Services.GetRequiredService<LanguageSelectionViewModel>();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!language.ConfirmCommand.CanExecute(null) && !startup.IsCompleted)
                await Task.Delay(10, deadline.Token);
            Assert.That(language.ConfirmCommand.CanExecute(null), Is.True);
            var disposal = host.DisposeAsync().AsTask();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.CatchAsync<OperationCanceledException>(async () => await startup);
            Assert.That(host.DisposeAsync().AsTask(), Is.SameAs(disposal));
            Assert.That(PlayerTextExtension.Resolve, Is.SameAs(previous));
            Assert.Throws<ObjectDisposedException>(() => host.InitializeAsync(null, null));
        }
        finally { await host.DisposeAsync(); }
    });

    [Test]
    public Task FailedCompositionReleasesAsyncServices() => Ui(async () =>
    {
        var previous = PlayerTextExtension.Resolve;
        var owned = new OwnedService();
        InvalidOperationException? failure = null;
        try { await AvaloniaPlayerHost.CreateAsync(Options(), services =>
        {
            Configure(services, "Fail");
            services.AddSingleton(_ => owned);
            services.AddScoped<IGameNavigationService>(provider =>
            {
                provider.GetRequiredService<OwnedService>();
                throw new InvalidOperationException("Composition failed.");
            });
        }); }
        catch (InvalidOperationException error) { failure = error; }
        Assert.That(failure, Is.Not.Null);
        Assert.That(owned.Disposals, Is.EqualTo(1));
        Assert.That(PlayerTextExtension.Resolve, Is.SameAs(previous));
    });

    [TestCase("../escape.png", typeof(InvalidDataException))]
    [TestCase("nested/file.png", typeof(ArgumentException))]
    [TestCase("alternate:stream.png", typeof(ArgumentException))]
    [TestCase("image.jpg", typeof(ArgumentException))]
    public Task StandaloneCaptureRejectsUnsafeNamesBeforeCreatingOutput(string name, Type errorType) => Ui(() =>
    {
        var error = Assert.CatchAsync(() => SceneScreenshotWriter.SaveAsync(new GamePage(), _root, name));
        Assert.That(error, Is.TypeOf(errorType));
        Assert.That(Directory.GetFiles(_root), Is.Empty);
        return Task.CompletedTask;
    });

    private sealed class OwnedService : IAsyncDisposable
    {
        public int Disposals { get; private set; }
        public async ValueTask DisposeAsync() { await Task.Yield(); Disposals++; }
    }

    private sealed class HostText(string label) : ILocalizationProvider
    {
        public string Name => "Host";
        public IEnumerable<CultureInfo> GetAvailableCultures() => [CultureInfo.GetCultureInfo("en-US")];
        public string GetString(string key, CultureInfo culture) => label;
        public bool TryGetString(string key, CultureInfo culture, out string? value) { value = label; return true; }
        public Task ReloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
