using GalNet.Media.LibVlc;
using GalNet.Avalonia.GameHost;
using GalNet.GameHost;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.Settings;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Variable;
using GalNet.Runtime.Gallery;
using GalNet.Runtime.Persistence;
using GalNet.Runtime.Variables;
using GalNet.Sample.Avalonia;
using GalNet.Sample.Avalonia.Services;
using GalNet.Storage.FileSystem;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.IntegrationTests;

[NonParallelizable]
public sealed class SampleCleanupTests
{
    [Test]
    public async Task OrderedCleanupRetainsSynchronousAsynchronousAndCancellationFailures()
    {
        var cleanup = new ResourceCleanup();
        var order = new List<int>();
        var errors = new Exception[] { new IOException("sync"), new InvalidDataException("async"), new OperationCanceledException("cancel") };
        cleanup.Run(() => { order.Add(1); throw errors[0]; });
        await cleanup.RunAsync(async () => { await Task.Yield(); order.Add(2); throw errors[1]; });
        await cleanup.RunAsync(() => { order.Add(3); throw errors[2]; });
        cleanup.Run(() => order.Add(4));
        var failure = Assert.Throws<AggregateException>(() => cleanup.ThrowIfFailed());
        Assert.That(order, Is.EqualTo(new[] { 1, 2, 3, 4 }));
        Assert.That(failure!.InnerExceptions, Is.EqualTo(errors));
    }

    [Test]
    public async Task PlayerSessionReleasesProgressAndSaveServiceAfterVariableFailure()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "galnet-cleanup-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var store = new Store();
            var variables = await PlayerVariableService.CreateAsync(store);
            var progress = new FileGameProgressService(directory, TimeSpan.FromHours(1));
            progress.MarkRead("group", "entry");
            var saves = new FileSaveService(directory);
            var session = new PlayerSession(saves, variables, progress, null!, _ => Task.CompletedTask);
            var error = Assert.Throws<AggregateException>(session.Dispose);
            Assert.That(error!.InnerExceptions.Select(item => item.Message), Is.EqualTo(new[] { "variables" }));
            Assert.That(File.Exists(Path.Combine(directory, "progress.json")), Is.True);
            Assert.That(store.Disposals, Is.EqualTo(1));
            Assert.ThrowsAsync<ObjectDisposedException>(() => saves.DeleteAsync(0));
            session.Dispose();
            Assert.That(store.Disposals, Is.EqualTo(1));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test]
    public async Task RealSessionClearsOwnedStateWhenSettingsFlushFails()
    {
        await SampleUiTests.RunAsync(async () =>
        {
            var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "galnet-session-cleanup-" + Guid.NewGuid().ToString("N"))).FullName;
            try
            {
                Directory.CreateDirectory(Path.Combine(directory, "Graph"));
                await File.WriteAllTextAsync(Path.Combine(directory, "Graph", "graph.json"), """{"version":2,"name":"cleanup","rootNodeId":"","nodes":[],"edges":[],"playerVariables":[],"saveVariables":[]}""");
                var services = new ServiceCollection();
                SampleCustomization.ConfigureServices(services, false);
                await using var root = services.BuildServiceProvider();
                var settings = root.GetRequiredService<AvaloniaPlayerSettingsService>();
                var gameplay = root.GetRequiredService<GamePageViewModel>();
                var session = new SampleGameSessionService(gameplay, new GamePage(), settings,
                    root.GetRequiredService<SampleLocalizationService>(), root.GetRequiredService<AvaloniaPlayerStartup>(),
                    root.GetRequiredService<LibVlcRuntime>());
                var profile = Path.Combine(directory, "profile");
                Assert.That(await session.InitializeAsync(new GameLaunchOptions(directory, profile), (_, _) => Task.FromResult<string?>("en-US")), Is.True);
                var variablesPath = Path.Combine(profile, "player-variables.json");
                File.Delete(variablesPath);
                Directory.CreateDirectory(variablesPath);
                var setting = settings.Categories.SelectMany(category => category.Items).OfType<BoolSetting>().Single(item => item.Id == "quickReadOnly");
                Assert.That(await setting.TrySetValueAsync(!setting.Value), Is.False);
                var closing = session.DisposeAsync().AsTask();
                AggregateException? failure = null;
                try { await closing; }
                catch (AggregateException error) { failure = error; }
                Assert.That(failure, Is.Not.Null);
                Assert.That(failure!.Flatten().InnerExceptions.Any(error => error is IOException or UnauthorizedAccessException), Is.True);
                Assert.That(session.GalleryResources, Is.Null);
                Assert.That(session.GalleryDataSource, Is.Null);
                Assert.That(session.DisposeAsync().AsTask(), Is.SameAs(closing));
            }
            finally { Directory.Delete(directory, true); }
        });
    }

    private sealed class Gallery : IGalleryResourceResolver, IDisposable
    {
        public int Disposals { get; private set; }
        public string? ResolvePath(string resourceId) => null;
        public void Dispose() { Disposals++; throw new IOException("gallery"); }
    }
    private sealed class Store : IPlayerVariableStore, IDisposable
    {
        public int Disposals { get; private set; }
        public Task<IReadOnlyDictionary<string, Variable>> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, Variable>>(new Dictionary<string, Variable>());
        public Task SaveAsync(IReadOnlyDictionary<string, Variable> variables, CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { Disposals++; throw new IOException("variables"); }
    }
}
