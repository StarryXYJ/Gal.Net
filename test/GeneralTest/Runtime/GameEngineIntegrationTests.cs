using GalNet.Core.Graph;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Loader;
using GalNet.Runtime.SaveLoad;
using GalNet.Presentation.Defaults;
using GalNet.Core.Entry;
using GalNet.Core.View;

namespace GeneralTest.Runtime;

public class GameEngineIntegrationTests
{
    [Test]
    public async Task Simple_Linear_Graph_Should_Run_To_Completion()
    {
        var graph = new GalNet.Core.Graph.Graph
        {
            Name = "Test",
            RootNodeId = "group_main",
            Nodes =
            {
                new Group
                {
                    Id = "group_main",
                    Name = "Main",
                    Entries =
                    {
                        Create(TextEntry.TypeId, 1, ("speaker", "Narrator"), ("content", "Test text"))
                    }
                }
            },
            Edges = { }
        };

        var view = new NullGameView();
        var engine = new GameEngine(graph, view);

        var finished = await engine.StepAsync();
        Assert.That(finished, Is.False);
    }

    [Test]
    public async Task Graph_With_Variable_Set_Then_Condition_Branch_Should_Take_Expected_Path()
    {
        var graph = new GalNet.Core.Graph.Graph
        {
            Name = "Test",
            RootNodeId = "group_setup",
            Nodes =
            {
                new Group
                {
                    Id = "group_setup",
                    Name = "Setup",
                    Entries =
                    {
                        Create(SetVariableEntry.TypeId, 1, ("target", "flag_route_a"), ("expression", "true"))
                    }
                },
                new Branch
                {
                    Id = "branch_check",
                    Name = "Check",
                    BranchType = BranchType.Condition,
                    Conditions =
                    {
                        new BranchCondition { Expression = "[flag_route_a] == true" },
                        new BranchCondition { Expression = "true" }
                    }
                },
                new Group
                {
                    Id = "group_a",
                    Name = "RouteA",
                    Entries =
                    {
                        Create(SetVariableEntry.TypeId, 1, ("target", "route_taken"), ("expression", "\"a\""))
                    }
                },
                new Group
                {
                    Id = "group_b",
                    Name = "RouteB",
                    Entries =
                    {
                        Create(SetVariableEntry.TypeId, 1, ("target", "route_taken"), ("expression", "\"b\""))
                    }
                }
            },
            Edges =
            {
                new Edge("group_setup", 0, "branch_check"),
                new Edge("branch_check", 0, "group_a"),
                new Edge("branch_check", 1, "group_b")
            }
        };

        var view = new NullGameView();
        var engine = new GameEngine(graph, view);

        await engine.StepAsync();

        var saveData = engine.CreateSaveData();
        var routeTaken = saveData.Variables.GetValueOrDefault("route_taken");
        Assert.That(routeTaken, Is.Not.Null);
        Assert.That(routeTaken!.AsString(), Is.EqualTo("a"));
    }

    [Test]
    public async Task Save_And_Restore_Should_Preserve_State()
    {
        var graph = new GalNet.Core.Graph.Graph
        {
            Name = "Test",
            RootNodeId = "group_main",
            Nodes =
            {
                new Group
                {
                    Id = "group_main",
                    Name = "Main",
                    Entries =
                    {
                        Create(TextEntry.TypeId, 1, ("speaker", "Narrator"), ("content", "Half way")),
                        Create(SetVariableEntry.TypeId, 2, ("target", "save_point"), ("expression", "true")),
                        Create(TextEntry.TypeId, 3, ("speaker", "Narrator"), ("content", "End"))
                    }
                }
            },
            Edges = { }
        };

        var view1 = new NullGameView();
        var engine1 = new GameEngine(graph, view1);
        await engine1.StepAsync();

        var saveData = engine1.CreateSaveData();
        var saveJson = SaveManager.Serialize(saveData);
        var restored = SaveManager.Deserialize(saveJson);

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Variables.GetValueOrDefault("save_point")!.AsBool(), Is.True);
    }

    [Test]
    public async Task Invalid_SetVariable_Expression_Should_Not_Overwrite_The_Previous_Value()
    {
        var graph = new GalNet.Core.Graph.Graph
        {
            Name = "Test",
            RootNodeId = "group_main",
            Nodes =
            {
                new Group
                {
                    Id = "group_main",
                    Name = "Main",
                    Entries =
                    {
                        Create(SetVariableEntry.TypeId, 1, ("target", "result"), ("expression", "\"before\"")),
                        Create(SetVariableEntry.TypeId, 2, ("target", "result"), ("expression", "1 +"))
                    }
                }
            }
        };

        var engine = new GameEngine(graph, new NullGameView());
        Assert.DoesNotThrowAsync(async () => await engine.StepAsync());

        Assert.That(engine.CreateSaveData().Variables["result"].AsString(), Is.EqualTo("before"));
    }

    [Test]
    public async Task Layer_Transition_Should_Update_SceneState_And_Use_Injected_Facade()
    {
        var graph = new GalNet.Core.Graph.Graph
        {
            RootNodeId = "group_main",
            Nodes =
            {
                new Group
                {
                    Id = "group_main",
                    Entries =
                    {
                        Create(ShowLayerEntry.TypeId, 1,
                            ("handleId", "background"), ("assetId", "school"), ("transform", "{}"),
                            ("transitionId", "custom.fade"), ("transitionDuration", "1.25"),
                            ("transitionBlocking", "true"), ("transitionParameters", "{\"curve\":\"easeIn\"}"))
                    }
                }
            }
        };

        var services = new RecordingGameView();
        IGameView facade = new CompositeGameView(services, services, services, services, services, services, services, services);
        var engine = new GameEngine(graph, facade);

        await engine.StepAsync();

        var layer = engine.Runtime.SceneState.Layers.Single();
        Assert.That(layer.Id, Is.EqualTo("background"));
        Assert.That(layer.AssetId, Is.EqualTo("school"));
        Assert.That(engine.Runtime.SceneInstances.TryGet<GalNet.Core.Scene.Layer>("background", out _), Is.True);
        Assert.That(services.LastTransition, Is.EqualTo(new TransitionRequest(
            "custom.fade", null, "school", TimeSpan.FromSeconds(1.25), true, "{\"curve\":\"easeIn\"}")));
    }

    [Test]
    public void Scene_instance_handles_are_type_checked_and_invalid_after_removal()
    {
        var runtime = new GalNet.Runtime.Runtime.GameRuntime(null);
        var layer = runtime.SceneInstances.GetOrAdd<GalNet.Core.Scene.Layer>("layer-handle", id => new() { Id = id, AssetId = "bg" });

        Assert.That(runtime.SceneInstances.GetAll<GalNet.Core.Scene.Layer>(), Is.EquivalentTo(new[] { layer }));
        Assert.That(runtime.SceneInstances.TryGet<GalNet.Core.Scene.Layer>("layer-handle", out var resolved), Is.True);
        Assert.That(resolved, Is.SameAs(layer));

        Assert.That(runtime.SceneInstances.Remove<GalNet.Core.Scene.Layer>("layer-handle", out var removed), Is.True);
        Assert.That(removed, Is.SameAs(layer));
        Assert.That(runtime.SceneInstances.TryGet<GalNet.Core.Scene.Layer>("layer-handle", out _), Is.False);
        Assert.That(runtime.SceneInstances.GetAll<GalNet.Core.Scene.Layer>(), Is.Empty);
    }

    [Test]
    public async Task Layer_replace_keeps_instance_transform_z_and_display_mode()
    {
        var graph = new GalNet.Core.Graph.Graph
        {
            RootNodeId = "group_main",
            Nodes =
            {
                new Group
                {
                    Id = "group_main",
                    Entries =
                    {
                        Create(ShowLayerEntry.TypeId, 1,
                            ("handleId", "hero"), ("assetId", "hero-neutral"),
                            ("transform", "{\"x\":120,\"y\":-40,\"rotationDegrees\":12,\"scaleX\":1.25,\"scaleY\":0.8}"),
                            ("z", "12"), ("displayMode", "Uniform")),
                        Create(ReplaceLayerEntry.TypeId, 2, ("handleId", "hero"), ("assetId", "hero-smile")),
                        Create(TextEntry.TypeId, 3, ("content", "pause"))
                    }
                }
            }
        };

        var engine = new GameEngine(graph, new NullGameView());
        await engine.StepAsync();

        var layer = engine.Runtime.SceneInstances.GetAll<GalNet.Core.Scene.Layer>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(layer.AssetId, Is.EqualTo("hero-smile"));
            Assert.That(layer.Transform.X, Is.EqualTo(120));
            Assert.That(layer.Transform.Y, Is.EqualTo(-40));
            Assert.That(layer.Transform.RotationDegrees, Is.EqualTo(12));
            Assert.That(layer.Transform.ScaleX, Is.EqualTo(1.25f));
            Assert.That(layer.Transform.ScaleY, Is.EqualTo(0.8f));
            Assert.That(layer.Z, Is.EqualTo(12));
            Assert.That(layer.DisplayMode, Is.EqualTo(GalNet.Core.Scene.LayerDisplayMode.Uniform));
        });
    }

    private static GalNet.Core.Entry.Entry Create(string type, int id, params (string Key, string Value)[] values) =>
        EntryRegistry.Create(type, id, values: values.ToDictionary(x => x.Key, x => x.Value));

    private sealed class RecordingGameView : NullGameView
    {
        public TransitionRequest? LastTransition { get; private set; }
        public override Task PlayTransitionAsync(TransitionRequest request, CancellationToken ct)
        {
            LastTransition = request;
            return Task.CompletedTask;
        }
    }
}
