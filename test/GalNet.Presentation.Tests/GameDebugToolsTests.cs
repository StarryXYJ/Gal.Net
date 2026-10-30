using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GalNet.Avalonia.Debugging;
using GalNet.Avalonia.Debugging.Views;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameHost.ViewModels;
using GalNet.Avalonia.Themes;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Debugging;
using GalNet.Presentation.Abstractions.View;
using GalNet.Storage.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GeneralTest.Presentation;

[NonParallelizable]
public sealed class GameDebugToolsTests
{
    private string _root = null!;

    [SetUp]
    public void Setup()
    {
        _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "galnet-debug-tools-" + Guid.NewGuid().ToString("N"))).FullName;
        Directory.CreateDirectory(Path.Combine(_root, "content", "Graph"));
        File.WriteAllText(Path.Combine(_root, "content", "Graph", "graph.json"),
            """{"version":2,"name":"Debug","rootNodeId":"main","nodes":[{"id":"main","type":"Branch","branchType":"Condition","conditions":[]}],"edges":[],"playerVariables":[],"saveVariables":[]}""");
    }

    [TearDown]
    public void Cleanup() => Directory.Delete(_root, true);

    private static Task Ui(Func<Task> action) =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly)
            .Dispatch(async () => { await action(); return true; }, TestContext.CurrentContext.CancellationToken);

    private static AvaloniaGamePresentationOptions Presentation() => new()
    {
        CreateAudioHost = (_, _) => new SilentAudio(),
        CreateVideoPresenter = (_, _, _) => new UnusedVideo()
    };

    private async Task Initialize(AvaloniaPlayerHost host)
    {
        var startup = host.InitializeAsync(Path.Combine(_root, "content"), Path.Combine(_root, "profile"));
        var language = host.Services.GetRequiredService<LanguageSelectionViewModel>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!startup.IsCompleted)
        {
            if (language.ConfirmCommand.CanExecute(null)) language.ConfirmCommand.Execute(null);
            await Task.Delay(10, timeout.Token);
        }
        Assert.That(await startup, Is.True);
    }

    [Test]
    public Task DebugSavesRespectOccupiedSlotsAndClearUsesTheHostRestartCallback() => Ui(async () =>
    {
        var restarts = 0;
        await using var host = await AvaloniaPlayerHost.CreateAsync(AvaloniaPlayerHostTests.Options(2, Presentation()), services =>
        {
            AvaloniaPlayerHostTests.Configure(services, "Tools");
            services.AddGameDebugTools(new() { RestartAsync = _ => { restarts++; return Task.CompletedTask; } }, new());
        });
        await Initialize(host);
        await host.Session.PrepareNewGameAsync();
        var actions = host.Services.GetRequiredService<GameDebugActions>();
        Assert.That((await actions.SaveDebugSnapshotAsync(DebugSaveKind.ProgressOnly)).SlotIndex, Is.EqualTo(0));
        Assert.That((await actions.SaveDebugSnapshotAsync(DebugSaveKind.GalGroupStart)).SlotIndex, Is.EqualTo(1));
        Assert.That((await actions.SaveDebugSnapshotAsync(DebugSaveKind.ProgressOnly)).RequiresOverwriteConfirmation, Is.True);
        Assert.That((await actions.SaveDebugSnapshotAsync(DebugSaveKind.ProgressOnly, true)).SlotIndex, Is.EqualTo(0));
        using (var saves = new FileSaveService(Path.Combine(_root, "profile"), 2))
            Assert.That((await saves.LoadAsync(0))?.NodeId, Is.EqualTo("main"));
        await actions.ClearPlayerStateAsync();
        Assert.That(restarts, Is.EqualTo(1));
        Assert.That(host.Session.CanContinue, Is.False);
        Assert.That(actions.CanOpenLogDirectory, Is.False);
        await actions.ReloadGameResourcesAsync();
        Assert.That(restarts, Is.EqualTo(2));
    });

    [TestCase(800, 450)]
    [TestCase(360, 640)]
    public Task EmbeddedLogsAndConfirmationsNeedNoExtraWindowAndDetachCleanly(int width, int height) => Ui(async () =>
    {
        var restarts = 0;
        var store = new GameDebugLogStore();
        await using var host = await AvaloniaPlayerHost.CreateAsync(AvaloniaPlayerHostTests.Options(), services =>
        {
            AvaloniaPlayerHostTests.Configure(services, "Tools");
            services.AddGameDebugTools(new() { RestartAsync = _ => { restarts++; return Task.CompletedTask; } }, store);
        });
        var overlay = host.Services.GetRequiredService<DebugToolsOverlay>();
        var model = host.Services.GetRequiredService<GameDebugViewModel>();
        overlay.Resources.MergedDictionaries.Add(GamePalette.Mono.CreateResources());
        overlay.GameContent = host.View;
        overlay.ToolsEnabled = true;
        var window = new Window { Content = overlay, Width = width, Height = height };
        window.Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());
        window.Show();
        try
        {
            window.UpdateLayout();
            var pending = model.ClearPlayerStateCommand.ExecuteAsync(null);
            Assert.That(overlay.FindControl<Grid>("PromptLayer")!.IsVisible, Is.True);
            Assert.That(pending.IsCompleted, Is.False);
            Assert.That(overlay.TryHandleBack(), Is.True);
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(restarts, Is.Zero);
            model.ShowLogCommand.Execute(null);
            window.UpdateLayout();
            var logs = (DebugLogView)overlay.FindControl<ContentControl>("LogContent")!.Content!;
            Assert.That(logs.Bounds.Height, Is.GreaterThan(0));
            Assert.That(overlay.FindControl<ContentControl>("GameContentHost")!.IsEnabled, Is.False);
            using var logger = new LoggerConfiguration().WriteTo.Sink(store).CreateLogger();
            for (var index = 0; index < 505; index++) logger.Information("Line {Index}", index);
            Dispatcher.UIThread.RunJobs();
            Assert.That(logs.Lines, Has.Count.EqualTo(GameDebugLogStore.Capacity));
            Assert.That(logs.Lines[^1].Message, Is.EqualTo("Line 504"));
            Assert.That(overlay.TryHandleBack(), Is.True);
            logger.Information("Queued before detach");
            window.Content = null;
            var detachedCount = logs.Lines.Count;
            logger.Information("After detach");
            Dispatcher.UIThread.RunJobs();
            Assert.That(logs.Lines, Has.Count.EqualTo(detachedCount));
            window.Content = overlay;
            model.ShowLogCommand.Execute(null);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.That(logs.Lines[^1].Message, Is.EqualTo("After detach"));
            Assert.That(logs.Lines.Count(line => line.Message == "Queued before detach"), Is.EqualTo(1));
            var closingPrompt = model.ClearPlayerStateCommand.ExecuteAsync(null);
            Assert.That(closingPrompt.IsCompleted, Is.False);
            overlay.Dispose();
            await closingPrompt.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(overlay.FindControl<ContentControl>("LogContent")!.Content, Is.Null);
            Assert.That(overlay.ToolsEnabled, Is.False);
            Assert.That(overlay.TryHandleBack(), Is.False);
            Assert.That(restarts, Is.Zero);
        }
        finally { window.Close(); }
    });

    [Test]
    public Task ExternalAdapterStopsItsHostRejectsOldRunsAndReconnectsAfterScopeReplacement() => Ui(async () =>
    {
        using var listener = new DebugListener(new("tools", "content"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        string? previousRun = null;
        for (var iteration = 0; iteration < 2; iteration++)
        {
            AvaloniaPlayerHost? current = null;
            await using var host = await AvaloniaPlayerHost.CreateAsync(AvaloniaPlayerHostTests.Options(2, Presentation(),
                engine => current!.Services.GetRequiredService<AvaloniaExternalDebugSession>().Attach(engine)), services =>
            {
                AvaloniaPlayerHostTests.Configure(services, "Tools");
                services.AddGameDebugTools(new() { RestartAsync = _ => Task.CompletedTask }, new());
            });
            current = host;
            await Initialize(host);
            var accepted = listener.AcceptAsync(timeout.Token);
            await host.Services.GetRequiredService<AvaloniaExternalDebugSession>().ConnectAsync(listener.Invitation, timeout.Token);
            await using var peer = await accepted;
            await host.Session.PrepareNewGameAsync();
            var status = await peer.RequestAsync("status", cancellationToken: timeout.Token);
            var run = status.GetProperty("run");
            var runId = run.GetProperty("runId").GetString()!;
            var generation = run.GetProperty("generation").GetInt64();
            Assert.That(runId, Is.Not.EqualTo(previousRun));
            var payload = JsonSerializer.SerializeToElement(new { });
            Assert.That((await peer.RequestAsync("inspect", new DebugCommand("inspect", runId, generation, payload), timeout.Token)).ValueKind, Is.EqualTo(JsonValueKind.Object));
            await peer.RequestAsync("stop", new DebugCommand("stop", runId, generation, payload), timeout.Token);
            Assert.That((await peer.RequestAsync("status", cancellationToken: timeout.Token)).GetProperty("run").ValueKind, Is.EqualTo(JsonValueKind.Null));
            InvalidOperationException? rejection = null;
            try { await peer.RequestAsync("inspect", new DebugCommand("inspect", runId, generation, payload), timeout.Token); }
            catch (InvalidOperationException error) { rejection = error; }
            Assert.That(rejection, Is.Not.Null);
            await peer.DisposeAsync();
            await host.Services.GetRequiredService<AvaloniaExternalDebugSession>().DisconnectAsync().WaitAsync(timeout.Token);
            previousRun = runId;
        }
    });

    private sealed class SilentAudio : IAudioPlaybackHost
    {
        public AudioBackendCapabilities Capabilities => AudioBackendCapabilities.None;
        public ISEffectCatalog SEffects => SEffectCatalog.Empty;
        public event Action<AudioBackendEvent>? EventReceived { add { } remove { } }
        public Task ApplyAsync(AudioBackendRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class UnusedVideo : IVideoPresenter
    {
        public Task PlayVideoAsync(VideoRenderRequest request, CancellationToken cancellationToken) => throw new AssertionException("No video expected");
        public void AdvanceVideo(string playbackId) { }
        public void StopVideo(string handleId) { }
    }
}
