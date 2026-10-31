using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Avalonia.Headless;
using System.Text.Json;
using GalNet.Assets;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Variable;
using GalNet.Editor.Shared.Services;
using GalNet.GameHost.Gallery;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Persistence;
using GalNet.Runtime.Progress;
using GalNet.Runtime.Runtime;
using GalNet.Runtime.Variables;
using GalNet.Storage.FileSystem;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GalNet.IntegrationTests;

public sealed class ResourceGalleryTests
{
    private string _root = null!;
    private string _built = null!;
    [OneTimeSetUp]
    public async Task BuildSample()
    {
        var repository = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "GalNet.slnx"))) repository = repository.Parent;
        _root = Path.Combine(Path.GetTempPath(), "galnet-resource-gallery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root); _built = Path.Combine(_root, "Output");
        var entries = new TargetProfileEntryCatalog(GalNet.Project.Contracts.ProjectSchemas.Modules);
        await new ProjectContentBuilder(entries).BuildAsync(Path.Combine(repository!.FullName, "GameTestCase"), _built);
    }
    [OneTimeTearDown] public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [TestCase("cg", 100)]
    [TestCase("cg", 101)]
    [TestCase("audio", 200)]
    [TestCase("audio", 201)]
    [TestCase("video", 300)]
    [TestCase("video", 301)]
    [TestCase("scene", 400)]
    [TestCase("scene", 401)]
    public async Task SampleGalleryResourceHasResolvableNamesAndCompiledMetadata(string galleryType, int unlockId)
    {
        var types = BuiltinResourceTypes.CreateCatalog();
        using var resources = await GameResourceScope.OpenAsync(_built, types, BuiltinGalleryTypes.CreateCatalog(types));
        var items = resources.Content.Gallery.GetItems(galleryType);
        Assert.That(items, Has.Count.EqualTo(2));
        var item = items.Single(item => item.Id == unlockId);
        var file = await resources.Assets.GetFileAsync(item.ResourceId);
        Assert.That(file, Is.Not.Null); Assert.That(file!.Length, Is.GreaterThan(0));
        Assert.That(file.Metadata.GalleryUnlockId, Is.EqualTo(unlockId));
        Assert.That(file.Metadata.DisplayNameKey, Is.EqualTo(item.DisplayNameKey));
        foreach (var locale in new[] { "zh-CN", "en-US" })
        {
            var resolver = resources.Content.Localization.CreateResolver(() => CultureInfo.GetCultureInfo(locale));
            Assert.That(resolver.Resolve(item.DisplayNameKey!), Is.Not.Empty.And.Not.EqualTo(item.DisplayNameKey));
        }
    }

    [TestCase(400)]
    [TestCase(401)]
    public async Task SceneReplayStopsAtGroupBoundaryAndCannotPersistUnlockOrReadProgress(int id)
    {
        var gallery = GalleryCatalog.Create(new()
        {
            Types = [new() { TypeId = "scene", ResourceTypeId = "group" }],
            Items = [new() { Id = 100, TypeId = "scene", ResourceId = "target" }, new() { Id = 999, TypeId = "scene", ResourceId = "other" }]
        });
        var store = new Store(); using var player = await PlayerVariableService.CreateAsync(store);
        player.ConfigureSystemVariables(GalleryUnlockVariable.CreateDefinitions(gallery));
        var progress = new Progress();
        var group = new Group { Id = "scene-" + id, ResourceId = "target", Entries = [new PrimitiveEntry("gallery.unlock", JsonSerializer.SerializeToElement(new { id = 100 }))] };
        var leaked = new Group { Id = "next", ResourceId = "other", Entries = [new PrimitiveEntry("gallery.unlock", JsonSerializer.SerializeToElement(new { id = 999 }))] };
        var source = new Graph { RootNodeId = group.Id, Nodes = [group, leaked], Edges = [new Edge { FromNodeId = group.Id, ToNodeId = leaked.Id }] };
        var replay = SceneGalleryGraph.Create(source, "target");
        using var state = await SceneGalleryState.CreateAsync(player, progress, gallery);
        using var view = new GameRegistration().AddBuiltins(gallery: gallery).BuildView();
        var runtime = new GameRuntime(null, replay.RootNodeId, state.Variables);
        await using var engine = new GameEngine(replay, runtime, view, progress: state.Progress);
        await engine.AdvanceAsync();
        Assert.That(state.Variables.GetSnapshot(VariableScope.Player)[GalleryUnlockVariable.GetName(100)].AsBool(), Is.True);
        Assert.That(state.Variables.GetSnapshot(VariableScope.Player)[GalleryUnlockVariable.GetName(999)].AsBool(), Is.False);
        Assert.That(player.GetSnapshot(VariableScope.Player)[GalleryUnlockVariable.GetName(100)].AsBool(), Is.False);
        state.Progress.MarkRead(group.Id, "line"); await state.Progress.FlushAsync(); await state.Variables.FlushPlayerVariablesAsync();
        Assert.That(progress.Writes, Is.Zero); Assert.That(store.Writes, Is.Zero);
        Assert.That(replay.Edges, Is.Empty); Assert.That(source.Edges, Has.Count.EqualTo(1));
    }
    [Explicit("Requires the native LibVLC deployment; exercises a real composed scene surface.")]
    [TestCase(400)]
    [TestCase(401)]
    public Task SceneGalleryRendersBothSampleScenesAndReturnsWithoutChangingPlayerProgress(int id) => SampleUiTests.RunAsync(async () =>
    {
        var original = GalNet.Sample.CrossPlatform.SampleCustomization.CreateHostOptions();
        GalNet.GameHost.PlayerSession? player = null;
        var options = new GalNet.Avalonia.GameHost.AvaloniaGameHostOptions
        {
            LaunchPolicy = original.LaunchPolicy,
            Content = original.Content,
            SelectProjectOptions = original.SelectProjectOptions,
            CreateContentCatalogs = original.CreateContentCatalogs,
            ConfigureExecution = original.ConfigureExecution,
            AttachPlayer = context =>
            {
                player = context.Player;
                var variable = new GalVariable { Name = GalleryUnlockVariable.GetName(id) }; variable.SetValue(true);
                player.Variables.NotifyVariableChanged(VariableScope.Player, variable.Name, variable);
                return new EmptyBinding();
            }
        };
        await using var host = await GalNet.Avalonia.GameHost.AvaloniaPlayerHost.CreateAsync(options,
            services => GalNet.Sample.CrossPlatform.SampleCustomization.ConfigureServices(services));
        Assert.That(await host.Session.InitializeAsync(_built, Path.Combine(_root, "profile-" + id), (_, _) => Task.FromResult<string?>("zh-CN")), Is.True, host.Session.StatusMessage);
        var navigation = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<GalNet.Avalonia.GameView.Navigation.IGameNavigationService>(host.Services);
        var model = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<GalNet.Sample.CrossPlatform.ViewModels.SceneGalleryPlayerPageViewModel>(host.Services);
        navigation.ResetTo<GalNet.Sample.CrossPlatform.ViewModels.SceneGalleryPageViewModel>(GalNet.Avalonia.GameView.Navigation.NavigationTransition.None);
        navigation.Navigate<GalNet.Sample.CrossPlatform.ViewModels.SceneGalleryPlayerPageViewModel>(GalNet.Avalonia.GameView.Navigation.NavigationTransition.None);
        host.Services.GetRequiredService<GalNet.Sample.CrossPlatform.Views.PlayerView>().CompleteStartup();
        var window = new global::Avalonia.Controls.Window { Width = 1000, Height = 600, Content = host.View }; window.Show();
        try
        {
            var item = host.Session.GalleryDataSource!.GetTypes().Single(type => type.Type.TypeId == "scene").Items.Single(item => item.Item.Id == id);
            await model.ActivateAsync(item);
            Assert.That(model.State.ErrorMessage, Is.Null.Or.Empty);
            window.UpdateLayout();
            Assert.That(model.State.Gameplay.IsGameToolbarVisible, Is.False);
            var replayMenus = (GalNet.Sample.CrossPlatform.ViewModels.GamePageViewModel)model.State.Surface.DataContext!;
            await replayMenus.OpenSaveSlotsCommand.ExecuteAsync(null); await replayMenus.OpenLoadSlotsCommand.ExecuteAsync(null);
            replayMenus.OpenSettingsCommand.Execute(null);
            Assert.That(navigation.CurrentViewModel, Is.SameAs(model), "Replay commands cannot enter the main session's save/load/settings pages.");
            var capture = Environment.GetEnvironmentVariable("GALNET_THEME_CAPTURE_DIR");
            if (capture is not null)
            {
                Directory.CreateDirectory(capture); global::Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var bitmap = window.CaptureRenderedFrame();
                bitmap!.Save(Path.Combine(capture, $"scene-replay-{id}.png"));
            }
            for (var attempt = 0; attempt < 150 && navigation.CurrentViewModel == model; attempt++)
            { model.State.Gameplay.AdvanceCommand.Execute(null); await Task.Delay(25); }
            Assert.That(navigation.CurrentViewModel, Is.TypeOf<GalNet.Sample.CrossPlatform.ViewModels.SceneGalleryPageViewModel>());
            var unlocked = id == 400 ? 100 : 101;
            Assert.That(player!.Variables.GetSnapshot(VariableScope.Player)[GalleryUnlockVariable.GetName(unlocked)].AsBool(), Is.False);
            Assert.That(player.Progress.IsRead(id == 400 ? "gallery-classroom" : "gallery-evening", "line-1"), Is.False);
            Assert.That(host.Session.IsPlaying, Is.False);
        }
        finally { window.Close(); }
    });
    [Explicit("Requires native LibVLC; validates both sample videos with actual decoding.")]
    [TestCase(300)]
    [TestCase(301)]
    public Task VideoGalleryCanPauseSeekResumeAndReleaseBothSampleVideos(int id) => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await GalNet.Avalonia.GameHost.AvaloniaPlayerHost.CreateAsync(
            GalNet.Sample.CrossPlatform.SampleCustomization.CreateHostOptions(),
            services => GalNet.Sample.CrossPlatform.SampleCustomization.ConfigureServices(services));
        Assert.That(await host.Session.InitializeAsync(_built, Path.Combine(_root, "video-profile-" + id), (_, _) => Task.FromResult<string?>("zh-CN")), Is.True, host.Session.StatusMessage);
        var model = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<GalNet.Sample.CrossPlatform.ViewModels.VideoGalleryPlayerPageViewModel>(host.Services);
        var media = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<GalNet.Avalonia.GameView.Services.GalleryMediaService>(host.Services);
        var item = host.Session.GalleryDataSource!.GetTypes().Single(type => type.Type.TypeId == "video").Items.Single(item => item.Item.Id == id);
        await model.ActivateAsync(item with { IsUnlocked = true });
        async Task WaitFor(Func<bool> condition)
        {
            for (var attempt = 0; attempt < 100 && !condition(); attempt++) await Task.Delay(50);
            Assert.That(condition(), Is.True, model.State.ErrorMessage);
        }
        try
        {
            await WaitFor(() => model.State.IsPlaying && model.State.CanSeek);
            model.State.TogglePlaybackCommand.Execute(null);
            await WaitFor(() => !model.State.IsPlaying);
            var pausedAt = media.VideoPosition;
            await Task.Delay(300);
            Assert.That(media.VideoPosition, Is.EqualTo(pausedAt).Within(250));
            model.State.BeginSeek(); model.State.Progress = 55;
            await Task.Delay(250);
            Assert.That(model.State.Progress, Is.EqualTo(55), "Backend polling must preserve an in-progress drag.");
            model.State.EndSeek();
            await WaitFor(() => Math.Abs(media.VideoPosition - media.VideoDuration * .55) < 800);
            model.State.TogglePlaybackCommand.Execute(null);
            await WaitFor(() => model.State.IsPlaying);
            model.BackCommand.Execute(null);
            Assert.That(media.HasVideo, Is.False); Assert.That(model.State.HasVideo, Is.False);
        }
        finally { model.Dispose(); }
    });

    [Explicit("Requires native LibVLC; validates retained music positions and seeking before playback.")]
    [TestCase(200)]
    [TestCase(201)]
    public Task MusicGalleryRetainsEachTrackPositionAndCanStartBySeeking(int firstId) => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await GalNet.Avalonia.GameHost.AvaloniaPlayerHost.CreateAsync(
            GalNet.Sample.CrossPlatform.SampleCustomization.CreateHostOptions(),
            services => GalNet.Sample.CrossPlatform.SampleCustomization.ConfigureServices(services));
        Assert.That(await host.Session.InitializeAsync(_built, Path.Combine(_root, "music-profile-" + firstId), (_, _) => Task.FromResult<string?>("zh-CN")), Is.True, host.Session.StatusMessage);
        var page = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<GalNet.Sample.CrossPlatform.ViewModels.AudioGalleryPageViewModel>(host.Services);
        var media = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<GalNet.Avalonia.GameView.Services.GalleryMediaService>(host.Services);
        var gallery = host.Session.GalleryDataSource!.GetTypes().Single(type => type.Type.TypeId == "audio");
        await page.ActivateAsync(new(gallery.Type, gallery.Items.Select(item => item with { IsUnlocked = true }).ToArray()));
        var first = page.State.Items.Single(item => item.Id == firstId);
        var second = page.State.Items.Single(item => item.Id != firstId);
        async Task WaitFor(Func<bool> condition)
        {
            for (var attempt = 0; attempt < 100 && !condition(); attempt++) await Task.Delay(50);
            Assert.That(condition(), Is.True, page.State.ErrorMessage);
        }
        try
        {
            Assert.That(first.CanSeek && second.CanSeek, Is.True);
            Assert.That(media.GetAudioPreview(first.Data).Duration, Is.GreaterThan(0));
            Assert.That(media.GetAudioPreview(second.Data).Duration, Is.GreaterThan(0));
            first.BeginSeek(); first.Progress = 25; first.EndSeek();
            await WaitFor(() => first.IsPlaying && Math.Abs(first.Progress - 25) < 3 && media.AudioPosition > 1000);
            Assert.That(second.IsPlaying, Is.False);
            page.State.ToggleAudioCommand.Execute(first);
            await WaitFor(() => !first.IsPlaying);
            var paused = media.GetAudioPreview(first.Data).Position;
            Assert.That(paused, Is.GreaterThan(1000));
            second.BeginSeek(); second.Progress = 45; second.EndSeek();
            await WaitFor(() => second.IsPlaying && Math.Abs(second.Progress - 45) < 3 && media.AudioPosition > 1000);
            await Task.Delay(300);
            Assert.That(media.GetAudioPreview(first.Data).Position, Is.EqualTo(paused).Within(300));
            page.State.ToggleAudioCommand.Execute(first);
            await WaitFor(() => first.IsPlaying && !second.IsPlaying);
            Assert.That(media.GetAudioPreview(first.Data).Position, Is.EqualTo(paused).Within(1000));
            Assert.That(second.Progress, Is.GreaterThan(40));
            Assert.That(first.TimeText, Does.Contain(" / "));
            page.BackCommand.Execute(null);
            Assert.That(media.IsAudioPlaying, Is.False);
            Assert.That(media.GetAudioPreview(second.Data).Progress, Is.GreaterThan(40));
        }
        finally { page.Dispose(); }
    });

    [Explicit("Requires native LibVLC; checks cancellation during duration parsing.")]
    [Test]
    public Task MusicDurationParsingCanBeCanceledDuringGalleryDisposal() => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await GalNet.Avalonia.GameHost.AvaloniaPlayerHost.CreateAsync(
            GalNet.Sample.CrossPlatform.SampleCustomization.CreateHostOptions(),
            services => GalNet.Sample.CrossPlatform.SampleCustomization.ConfigureServices(services));
        Assert.That(await host.Session.InitializeAsync(_built, Path.Combine(_root, "music-cancel-profile"), (_, _) => Task.FromResult<string?>("zh-CN")), Is.True);
        var media = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<GalNet.Avalonia.GameView.Services.GalleryMediaService>(host.Services);
        var items = host.Session.GalleryDataSource!.GetTypes().Single(type => type.Type.TypeId == "audio").Items;
        var preparations = items.Select(item => media.PrepareAudioAsync(item with { IsUnlocked = true })).ToArray();
        media.Dispose();
        try { await Task.WhenAll(preparations); } catch (OperationCanceledException) { }
        Assert.That(preparations.All(task => task.IsCompleted), Is.True);
    });

    private sealed class EmptyBinding : IDisposable { public void Dispose() { } }

    private sealed class Progress : IGameProgressService
    {
        public int Writes; public bool IsRead(string groupId, string entryId) => false;
        public void MarkRead(string groupId, string entryId) => Writes++;
    }
    private sealed class Store : IPlayerVariableStore
    {
        public int Writes;
        public Task<IReadOnlyDictionary<string, GalVariable>> LoadAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<string, GalVariable>>(new Dictionary<string, GalVariable>());
        public Task SaveAsync(IReadOnlyDictionary<string, GalVariable> variables, CancellationToken ct = default) { Writes++; return Task.CompletedTask; }
    }
}
