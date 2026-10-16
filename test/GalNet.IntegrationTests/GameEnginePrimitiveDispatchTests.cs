using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Core.Scene;
using GalNet.Core.Settings;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Engine;
using GraphModel = GalNet.Core.Graph.Graph;

namespace GeneralTest.Runtime;

public class GameEnginePrimitiveDispatchTests
{
    private static readonly int[] VideoSkipPoints = [5, 12];
    private static readonly string[] MarkDispatch = ["mark"];
    private static readonly string[] VisibleOptions = ["visible"];

    [TestCase(PlaybackMode.Quick, 0)]
    [TestCase(PlaybackMode.Quick, 1)]
    [TestCase(PlaybackMode.Quick, 2)]
    [TestCase(PlaybackMode.Auto, 0)]
    [TestCase(PlaybackMode.Auto, 1)]
    [TestCase(PlaybackMode.Auto, 2)]
    public async Task Video_segment_clicks_preserve_playback_boundary(PlaybackMode mode, int clicks)
    {
        var video = new SegmentedVideoPresenter();
        var markers = 0;
        var settings = new SettingsContainer();
        settings.Set(new GameSettings { QuickAdvanceIntervalMilliseconds = 0, AutoAdvanceIntervalMilliseconds = 0 });
        using var view = new GameRegistration().AddBuiltins(videoPresenter: video)
            .RegisterSchema(new DefaultEntryModule("test", [new DefaultPrimitiveEntryBase("test.mark", DynamicParameterTable.Empty)]))
            .RegisterInstruction("test.mark", context => new ImmediatePrimitiveInstance(() => Interlocked.Increment(ref markers), batchId: context.BatchId))
            .BuildView();
        await using var engine = new GameEngine(GraphWithGroup(
            Primitive("video.play", new { asset = "clip", skipPoints = VideoSkipPoints }),
            Primitive("test.mark")), view, settings: settings);
        await engine.AdvanceAsync();
        for (var index = 0; index < clicks; index++) await engine.AdvanceAsync();
        Assert.That(video.Advances, Is.EqualTo(clicks));
        await engine.SetPlaybackModeAsync(mode);
        if (mode == PlaybackMode.Auto)
        {
            Assert.That(video.End.Task.IsCompleted, Is.False, "Auto must not interrupt video playback.");
            Assert.That(Volatile.Read(ref markers), Is.Zero);
            video.End.TrySetResult();
        }
        await WaitUntilAsync(() => Volatile.Read(ref markers) == 1);
        Assert.That(engine.Runtime.State.Table<Layer>().Values, Is.Empty);
    }

    [Test]
    public void UncompiledAuthoringEntriesAreRejectedInsteadOfSilentlySkipped()
    {
        using var view = new GameRegistration().AddBuiltins().BuildView();
        Assert.Throws<InvalidOperationException>(() => new GameEngine(GraphWithGroup(new AuthoringPrimitiveEntry(BuiltinEntryIds.Wait)), view));
    }
    [Test]
    public async Task RestoreCancelsOldScopeAndIsolatesSceneAndSnapshots()
    {
        PrimitiveCreateContext? oldContext = null;
        using var view = View(Entry("test.wait", context =>
        {
            oldContext = context;
            return new ControlledInstance(true, false, null);
        }));
        using var engine = new GameEngine(GraphWithGroup(Primitive("test.wait")), view);
        var snapshot = engine.CreateSaveData();
        engine.Runtime.State.Table<Layer>().Add(new Layer { Id = "restored", AssetId = "saved" });
        snapshot.States["test.layers"] = engine.Runtime.State.Capture()["test.layers"];
        await engine.AdvanceAsync();

        engine.RestoreFrom(snapshot);
        oldContext!.Runtime.State.Table<Layer>().Clear();
        snapshot.States.Clear();
        var returned = engine.LastStableSnapshot;
        returned.States.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(oldContext.ScopeCancellation.IsCancellationRequested, Is.True);
            Assert.Throws<OperationCanceledException>(() => oldContext.Runtime.EndGame());
            Assert.That(engine.Runtime.State.Table<Layer>().Values.Single().AssetId, Is.EqualTo("saved"));
            Assert.That(GalNet.Core.Runtime.GameState.Restore(engine.Runtime.State.Catalog, engine.CreateSaveData().States).Table<Layer>().Values.Single().AssetId, Is.EqualTo("saved"));
        });
    }

    [Test]
    public async Task InvalidSaveVersionDoesNotCancelTheCurrentRun()
    {
        PrimitiveCreateContext? context = null;
        using var view = View(Entry("test.wait", value =>
        {
            context = value;
            return new ControlledInstance(true, false, null);
        }));
        using var engine = new GameEngine(GraphWithGroup(Primitive("test.wait")), view);
        await engine.AdvanceAsync();

        Assert.Throws<InvalidDataException>(() => engine.RestoreFrom(new GalNet.Core.Runtime.GameSnapshot { Version = -1 }));
        Assert.That(context!.ScopeCancellation.IsCancellationRequested, Is.False);
    }
    [Test]
    public async Task Video_segment_advances_hold_engine_and_previous_snapshot_until_natural_completion()
    {
        var video = new SegmentedVideoPresenter();
        var markers = 0;
        using var view = new GameRegistration().AddBuiltins(videoPresenter: video)
            .RegisterSchema(new DefaultEntryModule("test", [new DefaultPrimitiveEntryBase("test.mark", DynamicParameterTable.Empty)]))
            .RegisterInstruction("test.mark", context => new ImmediatePrimitiveInstance(() => markers++, batchId: context.BatchId))
            .BuildView();
        using var engine = new GameEngine(GraphWithGroup(
            Primitive("video.play", new { asset = "clip", skipPoints = VideoSkipPoints }),
            Primitive("test.mark")), view);
        await engine.AdvanceAsync();
        Assert.That(engine.CreateSaveData().EntryIndex, Is.Zero);
        await engine.AdvanceAsync();
        await engine.AdvanceAsync();
        Assert.Multiple(() =>
        {
            Assert.That(video.Advances, Is.EqualTo(2));
            Assert.That(markers, Is.Zero);
            Assert.That(GalNet.Core.Runtime.GameState.Restore(engine.Runtime.State.Catalog, engine.CreateSaveData().States).Table<Layer>().Values, Is.Empty);
        });
        video.End.TrySetResult();
        await WaitUntilAsync(() => markers == 1);
        Assert.That(engine.Runtime.State.Table<Layer>().Values, Is.Empty);
    }

    [Test]
    public async Task AdvanceRunsSynchronousContentUntilTheFirstUnfinishedBlocker()
    {
        var dispatched = new List<string>();
        var blocker = new ControlledInstance(true, true, "first");
        using var view = View(
            Entry("test.mark", context => new ImmediatePrimitiveInstance(() => dispatched.Add("mark"), batchId: context.BatchId)),
            Entry("test.wait", _ => blocker));
        using var engine = new GameEngine(GraphWithGroup(
            Primitive("test.mark"),
            Primitive("test.wait", "first"),
            Primitive("test.mark")), view);

        await engine.AdvanceAsync();

        Assert.Multiple(() =>
        {
            Assert.That(dispatched, Is.EqualTo(MarkDispatch));
            Assert.That(engine.EntryIndex, Is.EqualTo(2));
            Assert.That(blocker.SkipCount, Is.Zero);
        });
    }

    [Test]
    public async Task OneAdvanceSkipsTheExistingBatchThenStopsAtTheNextBatch()
    {
        var first = new ControlledInstance(true, true, "opening", completeOnSkip: true);
        var second = new ControlledInstance(true, true, "next");
        using var view = View(
            Entry("test.first", _ => first),
            Entry("test.second", _ => second));
        using var engine = new GameEngine(GraphWithGroup(
            Primitive("test.first", "opening"),
            Primitive("test.second", "next")), view);

        await engine.AdvanceAsync();
        await engine.AdvanceAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first.SkipCount, Is.EqualTo(1));
            Assert.That(second.WasDispatched, Is.True);
            Assert.That(second.SkipCount, Is.Zero);
        });
    }

    [Test]
    public async Task SameGroupBatchSkipsAlreadyDispatchedNonBlockingAndBlockingInstances()
    {
        var background = new ControlledInstance(false, true, "shared", completeOnSkip: true);
        var blocker = new ControlledInstance(true, true, "shared", completeOnSkip: true);
        using var view = View(
            Entry("test.background", _ => background),
            Entry("test.blocker", _ => blocker));
        using var engine = new GameEngine(GraphWithGroup(
            Primitive("test.background", "shared"),
            Primitive("test.blocker", "shared")), view);

        await engine.AdvanceAsync();
        await engine.AdvanceAsync();

        Assert.Multiple(() =>
        {
            Assert.That(background.SkipCount, Is.EqualTo(1));
            Assert.That(blocker.SkipCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SameBatchIdAcrossGroupsDoesNotAssociateInstances()
    {
        var oldBackground = new ControlledInstance(false, true, "shared");
        var blocker = new ControlledInstance(true, true, "shared", completeOnSkip: true);
        using var view = View(
            Entry("test.background", _ => oldBackground),
            Entry("test.blocker", _ => blocker));
        var graph = new GraphModel
        {
            RootNodeId = "first",
            Nodes =
            {
                new Group { Id = "first", Entries = { Primitive("test.background", "shared") } },
                new Group { Id = "second", Entries = { Primitive("test.blocker", "shared") } }
            },
            Edges = { new Edge { FromNodeId = "first", FromOutlet = 0, ToNodeId = "second" } }
        };
        using var engine = new GameEngine(graph, view);

        await engine.AdvanceAsync();
        await engine.AdvanceAsync();

        Assert.Multiple(() =>
        {
            Assert.That(oldBackground.SkipCount, Is.Zero);
            Assert.That(blocker.SkipCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task UnskippableBlockerDoesNotAllowAdvanceToPassThrough()
    {
        var blocker = new ControlledInstance(true, false, null);
        var later = 0;
        using var view = View(
            Entry("test.blocker", _ => blocker),
            Entry("test.later", context => new ImmediatePrimitiveInstance(() => later++, batchId: context.BatchId)));
        using var engine = new GameEngine(GraphWithGroup(
            Primitive("test.blocker"),
            Primitive("test.later")), view);

        await engine.AdvanceAsync();
        await engine.AdvanceAsync();

        Assert.Multiple(() =>
        {
            Assert.That(blocker.SkipCount, Is.Zero);
            Assert.That(later, Is.Zero);
        });
    }

    [Test]
    public async Task BlockingNaturalCompletionContinuesWithoutReceivingSkipPermission()
    {
        var first = new ControlledInstance(true, true, "first");
        var second = new ControlledInstance(true, true, "second");
        using var view = View(
            Entry("test.first", _ => first),
            Entry("test.second", _ => second));
        using var engine = new GameEngine(GraphWithGroup(
            Primitive("test.first", "first"),
            Primitive("test.second", "second")), view);

        await engine.AdvanceAsync();
        first.Complete();
        await WaitUntilAsync(() => second.WasDispatched);

        Assert.Multiple(() =>
        {
            Assert.That(first.SkipCount, Is.Zero);
            Assert.That(second.SkipCount, Is.Zero);
        });
    }

    [Test]
    public async Task NonBlockingPresentationDoesNotPreventStableSnapshot()
    {
        var pending = new ControlledInstance(false, false, null);
        using var view = View(Entry("test.async", _ => pending));
        var graph = new GraphModel
        {
            RootNodeId = "main",
            Nodes = { new Group { Id = "main", Entries = { Primitive("test.async") } }, new Group { Id = "after" } },
            Edges = { new Edge { FromNodeId = "main", ToNodeId = "after", FromOutlet = 0 } }
        };
        using var engine = new GameEngine(graph, view);

        await engine.AdvanceAsync();

        Assert.That(engine.CreateSaveData().NodeId, Is.EqualTo("after"));
        pending.Complete();
    }

    [Test]
    public async Task BlockingInstanceKeepsOldSnapshotUntilItCompletes()
    {
        var pending = new ControlledInstance(true, false, null);
        using var view = View(Entry("test.wait", _ => pending));
        var graph = new GraphModel
        {
            RootNodeId = "main",
            Nodes = { new Group { Id = "main", Entries = { Primitive("test.wait") } }, new Group { Id = "after" } },
            Edges = { new Edge { FromNodeId = "main", ToNodeId = "after", FromOutlet = 0 } }
        };
        using var engine = new GameEngine(graph, view);

        await engine.AdvanceAsync();
        Assert.That(engine.CreateSaveData().NodeId, Is.EqualTo("main"));

        pending.Complete();
        await WaitUntilAsync(() => engine.CreateSaveData().NodeId == "after");

        Assert.That(engine.CreateSaveData().NodeId, Is.EqualTo("after"));
    }

    [Test]
    public async Task RecommendedLayerDialogueAndChoiceRunThroughTheNewRuntimeBoundary()
    {
        var presenter = new StoryPresenter();
        using var view = new GameRegistration().AddBuiltins(presenter, presenter,
            characters: new GalNet.Core.I18n.CharacterCatalog([new("Alice", "character.alice.name")])).BuildView();
        var graph = new GraphModel
        {
            RootNodeId = "scene",
            Nodes =
            {
                new Group
                {
                    Id = "scene",
                    Entries =
                    {
                        Primitive(BuiltinEntryIds.ShowLayer, new
                        {
                            handleId = "hero",
                            assetId = "hero-a.png",
                            transform = new { x = 10, y = 20, scaleX = 1, scaleY = 1 },
                            z = 5,
                            opacity = 1,
                            displayMode = "Native"
                        }),
                        new PrimitiveEntry(BuiltinEntryIds.Text, JsonSerializer.SerializeToElement(new { speaker = "Alice", content = "story.continue" })) { StableId = "continue#1" }
                    }
                },
                new Branch
                {
                    Id = "choice",
                    BranchType = BranchType.Choice,
                    Options =
                    {
                        new BranchOption { Text = "hidden", Condition = "false" },
                        new BranchOption { Text = "choice.visible" }
                    }
                },
                new Group { Id = "hidden" },
                new Group
                {
                    Id = "visible",
                    Entries = { Primitive(BuiltinEntryIds.ReplaceLayer, new { handleId = "hero", assetId = "hero-b.png" }) }
                }
            },
            Edges =
            {
                new Edge { FromNodeId = "scene", FromOutlet = 0, ToNodeId = "choice" },
                new Edge { FromNodeId = "choice", FromOutlet = 0, ToNodeId = "hidden" },
                new Edge { FromNodeId = "choice", FromOutlet = 1, ToNodeId = "visible" }
            }
        };
        var runtime = new GalNet.Runtime.Runtime.GameRuntime(new StoryTextResolver(), graph.RootNodeId);
        using var engine = new GameEngine(graph, runtime, view, choicePresenter: presenter);

        await engine.AdvanceAsync();

        Assert.Multiple(() =>
        {
            Assert.That(presenter.ShownLayers.Single().AssetId, Is.EqualTo("hero-a.png"));
            Assert.That(presenter.Dialogue, Is.EqualTo(("Alice", "Continue?")));
            Assert.That(runtime.State.Table<Layer>().Values.Single().AssetId, Is.EqualTo("hero-a.png"));
        });

        await engine.AdvanceAsync();
        Assert.That(presenter.Options, Is.EqualTo(VisibleOptions));

        presenter.Select(0);
        await WaitUntilAsync(() => runtime.State.Table<Layer>().Values.Single().AssetId == "hero-b.png");

        Assert.That(presenter.ReplacedLayers, Does.Contain(("hero", "hero-b.png")));
    }

    [Test]
    public void MissingInstructionFailsBeforeStoryExecution()
    {
        using var view = View(Entry("test.mark", context => new ImmediatePrimitiveInstance(batchId: context.BatchId)));
        Assert.That(() => new GameEngine(GraphWithGroup(Primitive("missing.run")), view),
            Throws.InvalidOperationException.With.Message.Contains("missing.run"));
    }

    [Test]
    public void DispatchFailureIsReportedWithoutSilentlyAdvancing()
    {
        using var view = View(Entry("test.fail", _ => throw new InvalidOperationException("expected")));
        using var engine = new GameEngine(GraphWithGroup(Primitive("test.fail")), view);
        Assert.That(async () => await engine.AdvanceAsync(), Throws.InvalidOperationException.With.Message.Contains("expected"));
        Assert.That(engine.EntryIndex, Is.Zero);
    }

    private static GraphModel GraphWithGroup(params PrimitiveEntry[] entries)
    {
        var group = new Group { Id = "main" };
        group.Entries.AddRange(entries);
        return new GraphModel { RootNodeId = "main", Nodes = { group } };
    }

    private static PrimitiveEntry Primitive(string typeId, string? batchId = null) =>
        new(typeId, JsonSerializer.SerializeToElement(new { }), batchId);

    private static PrimitiveEntry Primitive(string typeId, object arguments, string? batchId = null) =>
        new(typeId, JsonSerializer.SerializeToElement(arguments), batchId);

    private static (string Type, Func<PrimitiveCreateContext, PrimitiveInstance> Factory) Entry(
        string name,
        Func<PrimitiveCreateContext, PrimitiveInstance> factory) =>
        (name, factory);

    private static CompositeGameView View(params (string Type, Func<PrimitiveCreateContext, PrimitiveInstance> Factory)[] entries) =>
        new GameRegistration()
            .RegisterSchema(new DefaultEntryModule("test", entries.Select(entry => new DefaultPrimitiveEntryBase(entry.Type, DynamicParameterTable.Empty))))
            .RegisterImplementation(new PrimitiveModule(registry =>
            {
                registry.State<Layer>("test.layers", layer => layer.Id);
                foreach (var entry in entries) registry.Register(entry.Type, entry.Factory);
            }))
            .BuildView();

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }


    private sealed class SegmentedVideoPresenter : IVideoPresenter
    {
        public TaskCompletionSource End { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Advances { get; private set; }
        public Task PlayVideoAsync(VideoRenderRequest request, CancellationToken cancellationToken) => End.Task.WaitAsync(cancellationToken);
        public void AdvanceVideo(string playbackId) => Advances++;
        public void StopVideo(string handleId) => End.TrySetResult();
    }

    private sealed class ControlledInstance : PrimitiveInstance
    {
        private readonly object _gate = new();
        private readonly bool _completeOnSkip;
        private bool _skippable;

        public ControlledInstance(bool blocking, bool skippable, string? batchId, bool completeOnSkip = false)
            : base(batchId)
        {
            IsBlocking = blocking;
            _skippable = skippable;
            _completeOnSkip = completeOnSkip;
        }

        public override bool IsBlocking { get; }
        public override bool IsSkippable { get { lock (_gate) return _skippable; } }
        public bool WasDispatched { get; private set; }
        public int SkipCount { get; private set; }

        protected override void OnDispatch() => WasDispatched = true;

        protected override void OnSkip()
        {
            lock (_gate)
            {
                if (IsCompleted || !_skippable)
                    return;
                SkipCount++;
                if (_completeOnSkip)
                {
                    _skippable = false;
                    TryComplete();
                }
            }
        }

        public void Complete()
        {
            lock (_gate)
            {
                _skippable = false;
                TryComplete();
            }
        }
    }

    private sealed class StoryPresenter : IChoicePresenter, IDialoguePresenter, ILayerPresenter
    {
        private readonly TaskCompletionSource<int> _selection = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<string> Options { get; private set; } = [];
        public List<LayerRenderRequest> ShownLayers { get; } = [];
        public List<(string HandleId, string AssetId)> ReplacedLayers { get; } = [];
        public (string Speaker, string Text) Dialogue { get; private set; }

        public Task<int> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellationToken)
        {
            Options = options;
            return _selection.Task.WaitAsync(cancellationToken);
        }

        public void Select(int index) => _selection.TrySetResult(index);

        public void ShowDialogue() { }
        public void HideDialogue() { }
        public Task PresentTextAsync(string speaker, string text, CancellationToken cancellationToken)
        {
            Dialogue = (speaker, text);
            return Task.CompletedTask;
        }
        public void SkipText() { }
        public void RevealAllText() { }
        public Task PresentTextImmediatelyAsync(string speaker, string text, CancellationToken cancellationToken) =>
            PresentTextAsync(speaker, text, cancellationToken);

        public void ShowLayer(LayerRenderRequest request) => ShownLayers.Add(request);
        public void ReplaceLayer(string handleId, string assetId) => ReplacedLayers.Add((handleId, assetId));
        public void HideLayer(string handleId) { }
        public void MoveLayer(string handleId, LayerTransform transform, float z, float durationSeconds) { }
    }

    private sealed class StoryTextResolver : GalNet.Core.Services.ITextResolver
    {
        public string Resolve(string key) => key switch
        {
            "character.alice.name" => "Alice",
            "story.continue" => "Continue?",
            "choice.visible" => "visible",
            _ => key
        };
    }
}
