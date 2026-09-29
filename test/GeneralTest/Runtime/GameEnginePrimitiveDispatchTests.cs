using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Engine;
using GraphModel = GalNet.Core.Graph.Graph;

namespace GeneralTest.Runtime;

public class GameEnginePrimitiveDispatchTests
{
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
            Assert.That(dispatched, Is.EqualTo(new[] { "mark" }));
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
        using var view = new CompositeGameView(BuiltinEntryModules.CreateRecommended(presenter, presenter));
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
                        Primitive(ShowLayerEntry.TypeId, new
                        {
                            handleId = "hero",
                            assetId = "hero-a.png",
                            transform = new { x = 10, y = 20, scaleX = 1, scaleY = 1 },
                            z = 5,
                            opacity = 1,
                            displayMode = "Native"
                        }),
                        Primitive(TextEntry.TypeId, new { speaker = "Alice", content = "Continue?", voice = "" }),
                        Primitive("unsupported.demo")
                    }
                },
                new Branch
                {
                    Id = "choice",
                    BranchType = BranchType.Choice,
                    Options =
                    {
                        new BranchOption { Text = "hidden", Condition = "false" },
                        new BranchOption { Text = "visible" }
                    }
                },
                new Group { Id = "hidden" },
                new Group
                {
                    Id = "visible",
                    Entries = { Primitive(ReplaceLayerEntry.TypeId, new { handleId = "hero", assetId = "hero-b.png" }) }
                }
            },
            Edges =
            {
                new Edge { FromNodeId = "scene", FromOutlet = 0, ToNodeId = "choice" },
                new Edge { FromNodeId = "choice", FromOutlet = 0, ToNodeId = "hidden" },
                new Edge { FromNodeId = "choice", FromOutlet = 1, ToNodeId = "visible" }
            }
        };
        var runtime = new GalNet.Runtime.Runtime.GameRuntime(null, graph.RootNodeId);
        using var engine = new GameEngine(graph, runtime, view, choicePresenter: presenter);

        await engine.AdvanceAsync();

        Assert.Multiple(() =>
        {
            Assert.That(presenter.ShownLayers.Single().AssetId, Is.EqualTo("hero-a.png"));
            Assert.That(presenter.Dialogue, Is.EqualTo(("Alice", "Continue?")));
            Assert.That(runtime.SceneState.Layers.Single().AssetId, Is.EqualTo("hero-a.png"));
        });

        await engine.AdvanceAsync();
        Assert.That(presenter.Options, Is.EqualTo(new[] { "visible" }));

        presenter.Select(0);
        await WaitUntilAsync(() => runtime.SceneState.Layers.Single().AssetId == "hero-b.png");

        Assert.That(presenter.ReplacedLayers, Does.Contain(("hero", "hero-b.png")));
    }

    [Test]
    public async Task UnknownPrimitiveAndDispatchFailureDoNotStopLaterEntries()
    {
        var completed = 0;
        using var view = View(
            Entry("test.fail", _ => throw new InvalidOperationException("expected")),
            Entry("test.mark", context => new ImmediatePrimitiveInstance(() => completed++, batchId: context.BatchId)));
        using var engine = new GameEngine(GraphWithGroup(
            Primitive("missing.run"),
            Primitive("test.fail"),
            Primitive("test.mark")), view);

        await engine.AdvanceAsync();

        Assert.That(completed, Is.EqualTo(1));
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

    private static DefaultPrimitiveEntryBase Entry(
        string name,
        Func<PrimitiveCreateContext, PrimitiveInstance> factory) =>
        new(name, DynamicParameterTable.Empty, factory);

    private static CompositeGameView View(params PrimitiveEntryBase[] entries) =>
        new([new TestModule("test", entries)]);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class TestModule(string id, IEnumerable<PrimitiveEntryBase> entries) : EntryModuleBase(id, entries);

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
        public void SetVoice(string assetId) { }
        public Task PresentTextAsync(string speaker, string text, CancellationToken cancellationToken)
        {
            Dialogue = (speaker, text);
            return Task.CompletedTask;
        }
        public void SkipText() { }

        public void ShowLayer(LayerRenderRequest request) => ShownLayers.Add(request);
        public void ReplaceLayer(string handleId, string assetId) => ReplacedLayers.Add((handleId, assetId));
        public void HideLayer(string handleId) { }
        public void MoveLayer(string handleId, LayerTransform transform, float z, float durationSeconds) { }
    }
}
