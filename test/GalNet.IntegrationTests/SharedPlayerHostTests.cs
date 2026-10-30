using Avalonia.Controls;
using GalNet.Avalonia.GameHost;
using GalNet.Core.Variable;
using GalNet.GameHost;
using GalNet.Runtime.Persistence;
using GalNet.Sample.Player;
using GalNet.Sample.Player.ViewModels;
using GalNet.Storage.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.MarkupExtensions;

namespace GalNet.IntegrationTests;

[NonParallelizable]
public sealed class SharedPlayerHostTests
{
    [Test]
    public Task ResourceFailuresReachPlayerLogsWithoutPreventingStartup() => SampleUiTests.RunAsync(async () =>
    {
        var previous = Serilog.Log.Logger;
        var sink = new AssetHostLogCapture();
        var configuration = new Serilog.LoggerConfiguration().WriteTo.Sink(sink);
#if GALNET_GAME_DEBUG
        var store = new GalNet.Sample.Avalonia.Debug.SampleDebugLogStore();
        store.Enable();
        configuration.WriteTo.Sink(store);
#endif
        using var logger = configuration.CreateLogger();
        Serilog.Log.Logger = logger;
        try
        {
            var defaults = SamplePlayerServices.CreateHostOptions();
            var options = new AvaloniaGameHostOptions
            {
                CreateContentCatalogs = defaults.CreateContentCatalogs,
                ConfigureExecution = defaults.ConfigureExecution,
                Content = defaults.Content,
                SelectProjectOptions = defaults.SelectProjectOptions,
                SelectTexturePreloads = async (assets, ct) =>
                {
                    Assert.That(await assets.AcquireAsync<byte[]>("diagnostic-missing", ct), Is.Null);
                    assets.RegisterProvider(new FailingDiagnosticProvider());
                    Assert.That(await assets.AcquireAsync<byte[]>("diagnostic-broken", ct), Is.Null);
                    return [];
                },
                SelectEffectPrograms = (_, _) => Task.FromResult(Array.Empty<GalNet.Contracts.Builtins.Scene.EffectProgramResource>()),
                Presentation = new()
                {
                    CreateAudioHost = (_, _) => new SilentAudio(),
                    CreateVideoPresenter = (_, _, _) => new UnusedVideo()
                }
            };
            await using var host = await SamplePlayerSession.CreateAsync(options);
            var startup = host.InitializeAsync(Path.Combine(_root, "content"), Path.Combine(_root, "diagnostic-profile"));
            var language = host.Services.GetRequiredService<LanguageSelectionViewModel>();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!startup.IsCompleted)
            {
                if (language.ConfirmCommand.CanExecute(null)) language.ConfirmCommand.Execute(null);
                await Task.Delay(10, timeout.Token);
            }
            Assert.That(await startup, Is.True);
            var reports = sink.Events.Where(item => item.Properties.ContainsKey("AssetFailure")).ToArray();
            Assert.That(reports, Has.Length.EqualTo(2));
            Assert.That(reports[0].RenderMessage(System.Globalization.CultureInfo.InvariantCulture), Does.Contain("Lookup").And.Contain("NotFound"));
            Assert.That(reports[1].RenderMessage(System.Globalization.CultureInfo.InvariantCulture), Does.Contain("ProviderFailure").And.Contain("test-provider"));
            Assert.That(reports[1].Exception?.Message, Is.EqualTo("diagnostic-provider-failure"));
            Assert.That(reports[1].Properties["LogChannel"].ToString(), Does.Contain("Game"));
#if GALNET_GAME_DEBUG
            var lines = store.GetSnapshot().Where(line => line.Message.Contains("diagnostic-", StringComparison.Ordinal)).ToArray();
            Assert.That(lines, Has.Length.EqualTo(2));
            Assert.That(lines[1].FullText, Does.Contain("diagnostic-provider-failure"));
#endif
        }
        finally { Serilog.Log.Logger = previous; }
    });

    private sealed class AssetHostLogCapture : Serilog.Core.ILogEventSink
    {
        public System.Collections.Concurrent.ConcurrentQueue<Serilog.Events.LogEvent> Events { get; } = new();
        public void Emit(Serilog.Events.LogEvent logEvent) => Events.Enqueue(logEvent);
    }

    private sealed class FailingDiagnosticProvider : GalNet.Core.Assets.IAssetProvider
    {
        public string Name => "test-provider";
        public bool Exists(string name) => true;
        public GalNet.Core.Assets.IArchive OpenArchive(string name) => throw new IOException("diagnostic-provider-failure");
        public Task<GalNet.Core.Assets.IArchive> OpenArchiveAsync(string name, CancellationToken ct = default) => throw new IOException("diagnostic-provider-failure");
    }

    [Test]
    public Task BackgroundCheckpointSurvivesScopeReplacementAndDoesNotUsePlayerSlots() => SampleUiTests.RunAsync(async () =>
    {
        File.WriteAllText(Path.Combine(_root, "content/Graph/graph.json"),
            """{"version":2,"name":"Resume","rootNodeId":"main","nodes":[{"id":"main","type":"Branch","branchType":"Condition","conditions":[]}],"edges":[],"playerVariables":[],"saveVariables":[]}""");
        var profile = Path.Combine(_root, "resume-profile");
        var defaults = SamplePlayerServices.CreateHostOptions();
        var options = new AvaloniaGameHostOptions
        {
            CreateContentCatalogs = defaults.CreateContentCatalogs,
            ConfigureExecution = defaults.ConfigureExecution,
            Content = defaults.Content,
            SelectProjectOptions = defaults.SelectProjectOptions,
            Presentation = new()
            {
                CreateAudioHost = (_, _) => new SilentAudio(),
                CreateVideoPresenter = (_, _, _) => new UnusedVideo()
            }
        };
        async Task<SamplePlayerSession> Open()
        {
            var host = await SamplePlayerSession.CreateAsync(options);
            var initialization = host.InitializeAsync(Path.Combine(_root, "content"), profile);
            var language = host.Services.GetRequiredService<LanguageSelectionViewModel>();
            while (!initialization.IsCompleted)
            {
                if (language.ConfirmCommand.CanExecute(null)) language.ConfirmCommand.Execute(null);
                await Task.Delay(10);
            }
            Assert.That(await initialization, Is.True);
            return host;
        }
        await using (var first = await Open())
        {
            await first.Session.PrepareNewGameAsync();
            using var manual = new FileSaveService(profile, 60);
            await manual.SaveAsync(0, new() { Snapshot = new() { NodeId = "manual-slot-sentinel" }, Description = "Keep my slot" });
            var original = Directory.GetFiles(Path.Combine(profile, "saves")).ToDictionary(path => path, File.ReadAllBytes);
            await first.SuspendAsync();
            Assert.That(first.Session.CanContinue, Is.True);
            Assert.That(first.Session.SaveSlots.Skip(1).All(slot => slot.IsEmpty), Is.True);
            foreach (var (path, bytes) in original) Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
            using var checkpoint = new FileSaveService(Path.Combine(profile, "resume"), 1);
            Assert.That((await checkpoint.LoadAsync(0))?.NodeId, Is.EqualTo("main"));
        }
        await using var reopened = await Open();
        Assert.That(reopened.Session.CanContinue, Is.True, "Resume must survive process/scope replacement.");
        // The manual sentinel cannot prepare this graph. Continue must choose the newer checkpoint.
        await reopened.Session.PrepareContinueAsync();
        await reopened.Session.ClearPlayerStateAsync();
        Assert.That(reopened.Session.CanContinue, Is.False);
        using var cleared = new FileSaveService(Path.Combine(profile, "resume"), 1);
        Assert.That(await cleared.LoadAsync(0), Is.Null);
    });

    [Test]
    public Task PlatformBackUsesPageCleanupAndCancelsAnExistingExitPrompt() => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await SamplePlayerSession.CreateAsync(SamplePlayerServices.CreateHostOptions());
        var navigation = host.Services.GetRequiredService<IGameNavigationService>();
        navigation.Navigate<GalNet.Avalonia.GameView.ViewModels.SettingsPageViewModel>(NavigationTransition.None);
        Assert.That(await host.HandleBackAsync(), Is.False);
        Assert.That(navigation.CurrentViewModel, Is.TypeOf<GalNet.Avalonia.GameView.ViewModels.TitlePageViewModel>());
        var prompt = host.HandleBackAsync();
        Assert.That(prompt.IsCompleted, Is.False);
        Assert.That(await host.HandleBackAsync(), Is.False);
        Assert.That(await prompt, Is.False);
    });

    private sealed class SilentAudio : GalNet.Contracts.Builtins.Audio.IAudioPlaybackHost
    {
        public GalNet.Contracts.Builtins.Audio.AudioBackendCapabilities Capabilities => GalNet.Contracts.Builtins.Audio.AudioBackendCapabilities.None;
        public GalNet.Contracts.Builtins.Audio.ISEffectCatalog SEffects => GalNet.Contracts.Builtins.Audio.SEffectCatalog.Empty;
        public event Action<GalNet.Contracts.Builtins.Audio.AudioBackendEvent>? EventReceived { add { } remove { } }
        public Task ApplyAsync(GalNet.Contracts.Builtins.Audio.AudioBackendRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class UnusedVideo : GalNet.Presentation.Abstractions.View.IVideoPresenter
    {
        public Task PlayVideoAsync(GalNet.Presentation.Abstractions.View.VideoRenderRequest request, CancellationToken cancellationToken) => throw new AssertionException("No video expected");
        public void AdvanceVideo(string playbackId) { }
        public void StopVideo(string handleId) { }
    }

    private string _root = null!;

    [SetUp]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "galnet-shared-player-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "content/Graph"));
        File.WriteAllText(Path.Combine(_root, "content/Graph/graph.json"),
            """{"version":2,"name":"Shared","rootNodeId":"","nodes":[],"edges":[],"playerVariables":[],"saveVariables":[]}""");
    }

    [TearDown]
    public void Cleanup() => Directory.Delete(_root, true);

    [Test]
    public async Task FailedCompositionDisposesAsyncServicesAndRestoresLocalization()
    {
        await SampleUiTests.RunAsync(async () =>
        {
            var previous = PlayerTextExtension.Resolve;
            var marker = new AsyncMarker();
            InvalidOperationException? failure = null;
            try { await SamplePlayerSession.CreateAsync(SamplePlayerServices.CreateHostOptions(), services =>
            {
                services.AddSingleton(_ => marker);
                services.AddScoped<IGameNavigationService>(provider =>
                {
                    provider.GetRequiredService<AsyncMarker>();
                    throw new InvalidOperationException("composition failed");
                });
            }); }
            catch (InvalidOperationException error) { failure = error; }
            Assert.That(failure, Is.Not.Null);
            Assert.That(marker.Disposals, Is.EqualTo(1));
            Assert.That(PlayerTextExtension.Resolve, Is.SameAs(previous));
        });
    }

    private sealed class AsyncMarker : IAsyncDisposable
    {
        public int Disposals { get; private set; }
        public async ValueTask DisposeAsync() { await Task.Yield(); Disposals++; }
    }

    [Test]
    public async Task DisposalCancelsPendingOnboardingAndReleasesTheSurface()
    {
        await SampleUiTests.RunAsync(async () =>
        {
            var host = await SamplePlayerSession.CreateAsync(SamplePlayerServices.CreateHostOptions());
            var window = new Window { Content = host.View, Width = 800, Height = 500 };
            window.Show();
            try
            {
                var startup = host.InitializeAsync(Path.Combine(_root, "content"), Path.Combine(_root, "profile"));
                var language = host.Services.GetRequiredService<LanguageSelectionViewModel>();
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (!language.ConfirmCommand.CanExecute(null) && !startup.IsCompleted && DateTime.UtcNow < deadline)
                    await Task.Delay(10);
                Assert.That(language.ConfirmCommand.CanExecute(null), Is.True, "Expected first-run language selection.");
                var disposal = host.DisposeAsync().AsTask();
                await disposal.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.CatchAsync<OperationCanceledException>(async () => await startup);
                Assert.That(host.DisposeAsync().AsTask(), Is.SameAs(disposal));
                Assert.Throws<ObjectDisposedException>(() => host.InitializeAsync(null, null));
                Assert.Throws<ObjectDisposedException>(() => host.Services.GetRequiredService<IAvaloniaPlayerSettings>());
                Assert.That(host.View.FindControl<GalNet.Game.Controls.GameCanvasHost>("GameCanvas")!.GameContent, Is.Null);
            }
            finally { window.Close(); await host.DisposeAsync(); }
        });
    }

    [Test]
    public async Task ReplacingTheScopeRetainsTheProfileAndCanResetOnboardingWithoutAnotherProcess()
    {
        await SampleUiTests.RunAsync(async () =>
        {
            var content = Path.Combine(_root, "content");
            var profile = Path.Combine(_root, "profile");
            async Task<SamplePlayerSession> Open()
            {
                var host = await SamplePlayerSession.CreateAsync(SamplePlayerServices.CreateHostOptions());
                var startup = host.InitializeAsync(content, profile);
                var language = host.Services.GetRequiredService<LanguageSelectionViewModel>();
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (!startup.IsCompleted && DateTime.UtcNow < deadline)
                {
                    if (language.ConfirmCommand.CanExecute(null)) language.ConfirmCommand.Execute(null);
                    await Task.Delay(10);
                }
                Assert.That(await startup.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
                return host;
            }
            var first = await Open();
            var firstView = first.View;
            await first.DisposeAsync();
            var second = await Open();
            try
            {
                Assert.That(second.View, Is.Not.SameAs(firstView));
                using (var variables = new FilePlayerVariableStore(profile))
                    Assert.That((await variables.LoadAsync())[PlayerStartup.LaunchCountVariable].AsInt(), Is.EqualTo(2));
                await second.Session.ClearPlayerStateAsync();
            }
            finally { await second.DisposeAsync(); }
            await using var third = await Open();
            using var reset = new FilePlayerVariableStore(profile);
            Assert.That((await reset.LoadAsync())[PlayerStartup.LaunchCountVariable].AsInt(), Is.EqualTo(1));
        });
    }
}
