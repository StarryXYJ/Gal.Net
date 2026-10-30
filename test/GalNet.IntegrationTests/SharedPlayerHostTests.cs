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
