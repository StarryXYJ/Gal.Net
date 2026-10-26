using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using DynamicLocalization.Core;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.Settings;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Execution;
using GalNet.Core.Variable;
using GalNet.Game.Controls.Forms;
using GalNet.Media.LibVlc;
using GalNet.Runtime.Variables;
using GalNet.Sample.Avalonia;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.IntegrationTests;

[NonParallelizable]
public sealed class SampleExtensibilityTests
{
    [Test]
    public Task ProjectTitleButtonNavigatesToOwnPageAndDefaultCommandCanBeOverridden() => SampleUiTests.RunAsync(async () =>
    {
        var services = Services();
        await using var root = services.BuildServiceProvider();
        await using var scope = root.CreateAsyncScope();
        var navigation = scope.ServiceProvider.GetRequiredService<IGameNavigationService>();
        navigation.ResetTo<TitlePageViewModel>(NavigationTransition.None);
        var title = (ProjectTitle)navigation.CurrentViewModel!;
        Assert.That(title, Is.SameAs(scope.ServiceProvider.GetRequiredService<TitlePageViewModel>()));
        title.OpenNotebookCommand.Execute(null);
        Assert.That(navigation.CurrentViewModel, Is.TypeOf<ProjectNotebook>());
        navigation.GoBack();
        Assert.That(navigation.CurrentViewModel, Is.SameAs(title));
        title.OpenSettingsCommand.Execute(null);
        Assert.That(navigation.CurrentViewModel, Is.TypeOf<ProjectNotebook>(), "Inherited RelayCommand dispatches the overridden method.");
    });

    [Test]
    public Task HandWrittenXamlUsesOwnModelAndProfileLifecycle() => SampleUiTests.RunAsync(async () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "galnet-extension-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "Graph"));
        await File.WriteAllTextAsync(Path.Combine(directory, "Graph", "graph.json"),
            """{"version":2,"name":"project","rootNodeId":"main","nodes":[{"id":"main","type":"Branch","branchType":"Condition","conditions":[]}],"edges":[],"playerVariables":[],"saveVariables":[]}""");
        try
        {
            await using var root = Services().BuildServiceProvider();
            await using var scope = root.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var defaults = SampleCustomization.CreateHostOptions();
            var releases = 0;
            var runAttaches = 0;
            var runReleases = 0;
            var settings = (ProjectSettings)services.GetRequiredService<IAvaloniaPlayerSettings>();
            var session = new AvaloniaGameSession(new()
            {
                CreateContentCatalogs = defaults.CreateContentCatalogs,
                ConfigureExecution = defaults.ConfigureExecution,
                Content = defaults.Content,
                SelectProjectOptions = defaults.SelectProjectOptions,
                AttachRun = engine =>
                {
                    runAttaches++;
                    Assert.That(engine.State, Is.EqualTo(GalNet.Runtime.Engine.GameEngineState.Ready));
                    if (runAttaches == 3) throw new IOException("Project attachment failed.");
                    return new Release(() => runReleases++);
                },
                AttachPlayer = context =>
                {
                    Assert.That(settings.Attached, Is.True);
                    Assert.That(context.ProfileDirectory, Is.EqualTo(Path.Combine(directory, "profile")));
                    return new Release(() => releases++);
                }
            }, services.GetRequiredService<GamePageViewModel>(), services.GetRequiredService<GalNet.Avalonia.GameView.Page.GamePage>(),
                settings, services.GetRequiredService<IPlayerLocalization>(), new SkipStartup(), services.GetRequiredService<LibVlcRuntime>());
            try
            {
                Assert.That(await session.InitializeAsync(directory, Path.Combine(directory, "profile"), (_, _) => Task.FromResult<string?>(null)), Is.True);
                var navigation = services.GetRequiredService<IGameNavigationService>();
                navigation.Navigate<SettingsPageViewModel>(NavigationTransition.None);
                var page = (ProjectSettingsPage)navigation.CurrentViewModel!;
                var view = (ProjectSettingsView)services.GetRequiredService<IPageViewFactory>().Create(page);
                Assert.That(view.FindControl<CheckBox>("Hints")!.IsChecked, Is.False);
                view.FindControl<CheckBox>("Hints")!.IsChecked = true;
                await page.ApplyCommand.ExecuteAsync(null);
                Assert.That(settings.Preferences.ShowHints, Is.True);
                Assert.That(File.ReadAllText(Path.Combine(directory, "profile", "player-variables.json")), Does.Contain("mygame.preferences"));
                Assert.That(settings.Categories, Is.Empty, "The handwritten page requires no generated DTO fields.");
                using var reopened = await PlayerVariableService.CreateAsync(new GalNet.Storage.FileSystem.FilePlayerVariableStore(Path.Combine(directory, "profile")));
                Assert.That(JsonSerializer.Deserialize<ProjectPreferences>(reopened.GetSnapshot(VariableScope.Player)["player.mygame.preferences"].AsString())!.ShowHints, Is.True);
                await session.PrepareNewGameAsync();
                await session.PrepareNewGameAsync();
                Assert.That(runReleases, Is.EqualTo(1), "Replacing a prepared run drains its binding.");
                await session.StopAsync();
                Assert.That(runReleases, Is.EqualTo(2));
                try { await session.PrepareNewGameAsync(); Assert.Fail("Attachment should fail."); }
                catch (IOException) { }
                await session.PrepareNewGameAsync();
            }
            finally { await session.DisposeAsync(); }
            Assert.That(releases, Is.EqualTo(1));
            Assert.That(runAttaches, Is.EqualTo(4));
            Assert.That(runReleases, Is.EqualTo(3), "Failed attach did not poison the session; async release ran exactly once per binding.");
            Assert.That(settings.Flushes, Is.GreaterThanOrEqualTo(1));
        }
        finally { Directory.Delete(directory, true); }
    });

    [Test]
    public void NeutralTextSourcesPreserveCulturePriorityNestedAndEmptyValues()
    {
        var culture = CultureInfo.GetCultureInfo("zh-CN");
        var content = new GalNet.GameHost.Content.GameLocalizationContent
        {
            Sources = [new("zh", "parent", """{"nested":{"text":"父语言"},"empty":""}"""), new("en-US", "default", """{"fallback":"default"}""")],
            ExternalSources = [new("en-US", "external", """{"nested.text":"external default"}""")]
        };
        var resolver = content.CreateResolver(() => culture);
        Assert.That(resolver.Resolve("nested.text"), Is.EqualTo("父语言"));
        Assert.That(resolver.Resolve("empty"), Is.Empty);
        Assert.That(resolver.Resolve("fallback"), Is.EqualTo("default"));
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddScoped<TitlePageViewModel, ProjectTitle>();
        services.AddScoped<SettingsPageViewModel, ProjectSettingsPage>();
        services.AddScoped<ProjectNotebook>();
        SampleCustomization.ConfigureServices(services, false, views =>
        {
            views.Register<ProjectTitle, UserControl>();
            views.Register<ProjectNotebook, UserControl>();
            views.Register<ProjectSettingsPage, ProjectSettingsView>();
        });
        services.AddScoped<IAvaloniaPlayerSettings, ProjectSettings>();
        return services;
    }

    public sealed class ProjectNotebook : PageViewModelBase;
    public sealed class ProjectTitle : TitlePageViewModel
    {
        private readonly IGameNavigationService _navigation;
        public ProjectTitle(IGameSessionService session, IGameNavigationService navigation, GameLaunchFlow flow, GalleryNavigationService gallery)
            : base(session, navigation, flow, gallery)
        {
            _navigation = navigation;
            OpenNotebookCommand = new RelayCommand(() => navigation.Navigate<ProjectNotebook>(NavigationTransition.None));
        }
        public IRelayCommand OpenNotebookCommand { get; }
        protected override void OpenSettings() => _navigation.Navigate<ProjectNotebook>(NavigationTransition.None);
    }

    private sealed class SkipStartup : IPlayerStartup
    {
        public Task<bool> InitializeAsync(PlayerVariableService variables, string culture,
            Func<string, CancellationToken, Task<string?>> select, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class Release(Action release) : IDisposable, IAsyncDisposable
    {
        public void Dispose() => throw new InvalidOperationException("The host must prefer asynchronous draining.");
        public async ValueTask DisposeAsync() { await Task.Yield(); release(); }
    }
}

public sealed class ProjectPreferences { public bool ShowHints { get; set; } }

public sealed class ProjectSettings : IAvaloniaPlayerSettings
{
    private PlayerVariableService? _variables;
    public ProjectPreferences Preferences { get; private set; } = new();
    public bool Attached { get; private set; }
    public int Flushes { get; private set; }
    public IReadOnlyList<SettingCategory> Categories => [];
    public EditorRegistry Editors { get; } = EditorRegistry.CreateDefault();
    public PlaybackOptions PlaybackOptions => new();
    public AudioMixOptions AudioMix => new();
    public double TextSpeed => 0;
    public event Action? SettingsChanged;
    public Task AttachAsync(PlayerVariableService variables, AudioConfiguration audio, bool tracks, CancellationToken cancellationToken = default)
    {
        _variables = variables;
        Attached = true;
        if (variables.GetSnapshot(VariableScope.Player).TryGetValue("player.mygame.preferences", out var stored))
            Preferences = JsonSerializer.Deserialize<ProjectPreferences>(stored.AsString())!;
        return Task.CompletedTask;
    }
    public async Task ApplyAsync()
    {
        var value = new Variable { Name = "player.mygame.preferences", Value = VariableValue.From(JsonSerializer.Serialize(Preferences)) };
        await _variables!.UpdatePlayerVariablesAsync(new Dictionary<string, Variable> { [value.Name] = value });
        SettingsChanged?.Invoke();
    }
    public Task FlushAsync() { Flushes++; return Task.CompletedTask; }
}

public sealed class ProjectSettingsPage : SettingsPageViewModel
{
    public ProjectSettingsPage(IGameNavigationService navigation, IPlayerSettingsService settings) : base(navigation, settings)
    {
        var project = (ProjectSettings)settings;
        Preferences = project.Preferences;
        ApplyCommand = new AsyncRelayCommand(project.ApplyAsync);
    }
    public ProjectPreferences Preferences { get; }
    public IAsyncRelayCommand ApplyCommand { get; }
}

public partial class ProjectSettingsView : UserControl
{
    public ProjectSettingsView() => AvaloniaXamlLoader.Load(this);
}
