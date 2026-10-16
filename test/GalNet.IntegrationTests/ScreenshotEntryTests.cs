using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.Themes;
using GalNet.Core.Compilation;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Core.Serialization;
using GalNet.Presentation.Abstractions.View;
using GalNet.Project.Contracts;
using GalNet.Rendering.Scene;
using GalNet.Runtime.Engine;
using GalNet.Sample.Avalonia.Presentation;
using SkiaSharp;

namespace GeneralTest.Integration;

[NonParallelizable]
public sealed class ScreenshotEntryTests
{
    private string _root = null!;
    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (!File.Exists(Path.Combine(directory.FullName, "GalNet.slnx"))) directory = directory.Parent!;
            return directory.FullName;
        }
    }
    [SetUp] public void SetUp() => _root = Directory.CreateDirectory(Path.Combine(RepositoryRoot, "Temp/F-20261013-01-screenshot-tests", Guid.NewGuid().ToString("N"))).FullName;
    [TearDown] public void TearDown() => Directory.Delete(_root, true);

    [TestCase(PrimitiveInvocationMode.Normal)]
    [TestCase(PrimitiveInvocationMode.Quick)]
    public async Task ConcreteGamePage_CapturesRealScenePng_AndQuickStillCaptures(PrimitiveInvocationMode mode)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ScreenshotTestApplication));
        await DispatchAsync(session, async () =>
        {
            var (page, window) = Scene();
            try
            {
                var instance = new ScreenshotPrimitiveInstance(page, () => _root, "scene.png", "batch", default);
                instance.Dispatch(mode);
                Assert.That(instance.IsBlocking, Is.True);
                Assert.That(instance.IsSkippable, Is.False);
                await instance.PresentationCompletion.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.That(instance.IsCompleted, Is.True);
                Assert.That(instance.BatchId, Is.EqualTo("batch"));
                using var png = SKBitmap.Decode(instance.SavedPath);
                Assert.That(png.Width, Is.EqualTo((int)Math.Ceiling(page.SceneHost.Bounds.Width)));
                Assert.That(png.Height, Is.EqualTo((int)Math.Ceiling(page.SceneHost.Bounds.Height)));
                Assert.That(png.GetPixel(5, 5), Is.EqualTo(SKColor.Parse("#203040")));
                Assert.That(png.GetPixel(png.Width / 2, png.Height / 2), Is.EqualTo(SKColor.Parse("#f0b040")));
                Assert.That(Directory.GetFiles(_root).Select(Path.GetFileName), Is.EqualTo(new[] { "scene.png" }));
                File.Copy(instance.SavedPath!, Path.Combine(RepositoryRoot, "Temp/F-20261013-01-screenshot-proof.png"), true);
            }
            finally { window.Close(); }
        }, TestContext.CurrentContext.CancellationToken);
    }

    [Test]
    public async Task CompiledSchemaToModuleToEngine_ExecutesAndSurfacesAsyncCaptureFailure()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ScreenshotTestApplication));
        await DispatchAsync(session, async () =>
        {
            var (page, window) = Scene();
            try
            {
                var catalog = new TargetProfileEntryCatalog(ProjectSchemas.Modules);
                var source = new GroupDocument
                {
                    Entries = [new GroupEntryDocument
                { Id = "capture-entry", Type = ScreenshotSchema.TypeId, Parameters = new() { ["fileName"] = JsonSerializer.SerializeToElement("engine.png") } }]
                };
                var compiled = GalgroupCompiler.Compile(source, catalog).Document.Entries;
                var group = new Group
                {
                    Id = "capture",
                    Entries = compiled.Select(entry => (Entry)new PrimitiveEntry(entry.TypeId, entry.Arguments, entry.BatchId)
                    { StableId = entry.Id, Condition = entry.Condition }).ToList()
                };
                var graph = new Graph { RootNodeId = group.Id, Nodes = [group] };
                using var registration = new GameRegistration().UseCatalog(catalog).RegisterImplementation(new SampleScreenshotModule(page, () => _root));
                await using var view = registration.BuildView();
                view.RequireInstructions(compiled.Select(entry => entry.TypeId));
                await using (var engine = new GameEngine(graph, view))
                {
                    var terminal = Terminal(engine);
                    await Task.Run(() => engine.AdvanceAsync());
                    Assert.That((await terminal.WaitAsync(TimeSpan.FromSeconds(5))).State, Is.EqualTo(GameEngineState.Completed));
                    Assert.That(File.Exists(Path.Combine(_root, "engine.png")), Is.True);
                }
                // This page has no layout. Dispatch starts off the UI thread, so failure is asynchronous.
                using var failedRegistration = new GameRegistration().UseCatalog(catalog).RegisterImplementation(new SampleScreenshotModule(new GamePage(), () => Path.Combine(_root, "failed")));
                await using var failedView = failedRegistration.BuildView();
                await using var failedEngine = new GameEngine(graph, failedView);
                var fault = Terminal(failedEngine);
                await Task.Run(() => failedEngine.AdvanceAsync());
                var result = await fault.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.That(result.State, Is.EqualTo(GameEngineState.Faulted));
                Assert.That(result.Error, Is.TypeOf<InvalidOperationException>());
                Assert.That(Directory.Exists(Path.Combine(_root, "failed")), Is.False);
            }
            finally { window.Close(); }
        }, TestContext.CurrentContext.CancellationToken);
    }

    [Test]
    public async Task CatchUp_Cancellation_AndConflict_PublishNoExtraFiles()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ScreenshotTestApplication));
        await DispatchAsync(session, async () =>
        {
            var catchUp = new ScreenshotPrimitiveInstance(new GamePage(), () => throw new InvalidOperationException("must not resolve directory"), null, null, default);
            catchUp.Dispatch(PrimitiveInvocationMode.CatchUp);
            await catchUp.PresentationCompletion;
            Assert.That(catchUp.IsCompleted, Is.True);
            var (page, window) = Scene();
            try
            {
                using var cancellation = new CancellationTokenSource();
                var cancelled = new ScreenshotPrimitiveInstance(page, () => _root, null, null, cancellation.Token);
                // Hold the UI worker until off-thread dispatch has queued capture, then cancel it.
                Task.Run(() => cancelled.Dispatch()).GetAwaiter().GetResult();
                cancellation.Cancel();
                try { await cancelled.PresentationCompletion; Assert.Fail("Expected cancellation."); }
                catch (OperationCanceledException) { }
                Assert.That(Directory.GetFiles(_root), Is.Empty);
                File.WriteAllText(Path.Combine(_root, "conflict.png"), "original");
                var conflict = new ScreenshotPrimitiveInstance(page, () => _root, "conflict.png", null, default);
                conflict.Dispatch();
                Assert.ThrowsAsync<IOException>(async () => await conflict.PresentationCompletion);
                Assert.That(File.ReadAllText(Path.Combine(_root, "conflict.png")), Is.EqualTo("original"));
                Assert.That(Directory.GetFiles(_root).Select(Path.GetFileName), Is.EqualTo(new[] { "conflict.png" }));
            }
            finally { window.Close(); }
        }, TestContext.CurrentContext.CancellationToken);
    }

    [Test]
    public async Task GeneratedNames_AreUnique_AndRelativeOrLinkedDirectoriesAreRejected()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ScreenshotTestApplication));
        await DispatchAsync(session, async () =>
        {
            var (page, window) = Scene();
            try
            {
                var names = new List<string?>();
                for (var index = 0; index < 2; index++)
                {
                    var instance = new ScreenshotPrimitiveInstance(page, () => _root, null, null, default);
                    instance.Dispatch(); await instance.PresentationCompletion;
                    names.Add(instance.SavedPath);
                }
                Assert.That(names.Distinct().Count(), Is.EqualTo(2));
                var relative = new ScreenshotPrimitiveInstance(page, () => "relative", null, null, default);
                relative.Dispatch();
                Assert.ThrowsAsync<InvalidDataException>(async () => await relative.PresentationCompletion);
                var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
                var link = Path.Combine(_root, "link");
                Directory.CreateSymbolicLink(link, target);
                try
                {
                    var linked = new ScreenshotPrimitiveInstance(page, () => link, null, null, default);
                    linked.Dispatch();
                    Assert.ThrowsAsync<InvalidDataException>(async () => await linked.PresentationCompletion);
                    Assert.That(Directory.GetFiles(target), Is.Empty);
                }
                finally { Directory.Delete(link); }
            }
            finally { window.Close(); }
        }, TestContext.CurrentContext.CancellationToken);
    }

    [Test]
    public async Task DisposingModule_CancelsAndDrainsQueuedCaptureBeforeSceneRelease()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ScreenshotTestApplication));
        await DispatchAsync(session, async () =>
        {
            var (page, window) = Scene();
            try
            {
                using var registration = new GameRegistration().UseCatalog(new TargetProfileEntryCatalog(ProjectSchemas.Modules))
                    .RegisterImplementation(new SampleScreenshotModule(page, () => _root));
                await using var view = registration.BuildView();
                var group = new Group { Id = "capture", Entries = [new PrimitiveEntry(ScreenshotSchema.TypeId, JsonSerializer.SerializeToElement(new { })) { StableId = "entry" }] };
                await using var engine = new GameEngine(new Graph { RootNodeId = group.Id, Nodes = [group] }, view);
                Task.Run(() => engine.AdvanceAsync()).GetAwaiter().GetResult();
                await view.DisposeAsync();
                Assert.That(Directory.GetFiles(_root), Is.Empty);
            }
            finally { window.Close(); }
        }, TestContext.CurrentContext.CancellationToken);
    }

    [Test]
    public async Task DisposedModuleRejectsDispatchAndUndispatchedScopeDoesNotBlockCleanup()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ScreenshotTestApplication));
        await DispatchAsync(session, async () =>
        {
            using var scope = new GalNet.Presentation.Abstractions.Execution.PrimitiveTaskScope();
            var page = new GamePage();
            await using var module = new SampleScreenshotModule(page, () => _root);
            var catalog = new TargetProfileEntryCatalog(ProjectSchemas.Modules);
            await using var view = new CompositeGameView(catalog, [module]);
            var entry = new PrimitiveEntry(ScreenshotSchema.TypeId, JsonSerializer.SerializeToElement(new { }));
            var instance = new ScreenshotPrimitiveInstance(scope, page, () => _root, null, null, default, scope.CancellationToken);
            await module.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Throws<ObjectDisposedException>(() => view.Dispatch(entry, new GalNet.Runtime.Runtime.GameRuntime(null), default));
            await scope.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            instance.Dispatch();
            try { await instance.PresentationCompletion; Assert.Fail("Expected scope cancellation."); }
            catch (OperationCanceledException) { }
            Assert.That(instance.PresentationCompletion.IsCanceled, Is.True);
            Assert.That(Directory.GetFiles(_root), Is.Empty);
        }, TestContext.CurrentContext.CancellationToken);
    }

    [TestCase("../escape.png")]
    [TestCase("C:\\escape.png")]
    [TestCase("folder/file.png")]
    [TestCase("CON.png")]
    [TestCase("screen.jpg")]
    [TestCase(" ")]
    [TestCase(null)]
    public void ContractRejectsUnsafeOrNullNames(string? fileName)
    {
        var contract = ScreenshotSchema.CreateModule().PrimitiveEntries[ScreenshotSchema.TypeId].Parameters;
        Assert.That(() => contract.ValidateArguments(JsonSerializer.SerializeToElement(new { fileName })), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void SchemaCanCompileWithoutAnyPlatformImplementation_WhileMissingExecutionIsExplicit()
    {
        var catalog = new TargetProfileEntryCatalog(ProjectSchemas.Modules);
        var source = new GroupDocument { Entries = [new() { Id = "capture", Type = ScreenshotSchema.TypeId }] };
        var compiled = GalgroupCompiler.Compile(source, catalog);
        using var view = new CompositeGameView(catalog, []);
        Assert.That(() => view.RequireInstructions(compiled.Document.Entries.Select(entry => entry.TypeId)), Throws.InvalidOperationException.With.Message.Contains(ScreenshotSchema.TypeId));
        Assert.That(typeof(ScreenshotSchema).Assembly.GetReferencedAssemblies().Select(name => name.Name), Does.Not.Contain("Avalonia"));
    }

    private static Task DispatchAsync(HeadlessUnitTestSession session, Func<Task> action, CancellationToken token) =>
        session.Dispatch<bool>(async () => { await action(); return true; }, token);

    private static Task<GameEngineStateChange> Terminal(GameEngine engine)
    {
        var completion = new TaskCompletionSource<GameEngineStateChange>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.StateChanged += result => { if (result.State is GameEngineState.Completed or GameEngineState.Faulted) completion.TrySetResult(result); };
        return completion.Task;
    }

    private static (GamePage Page, Window Window) Scene()
    {
        var page = new GamePage();
        var root = new Border { Child = page, Classes = { "galnet-player" } };
        root.Styles.Add(new ClassicTheme().CreateStyles());
        var window = new Window { Width = 640, Height = 360, Content = root };
        window.Show();
        window.UpdateLayout();
        page.SceneHost.ItemsSource = new ObservableCollection<SceneLayerItem>
        {
            new() { HandleId = "background", Color = "#203040" },
            new() { HandleId = "square", Color = "#f0b040", ScaleX = .5, ScaleY = .5, Z = 1 }
        };
        return (page, window);
    }
}

public static class ScreenshotTestApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>().UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
