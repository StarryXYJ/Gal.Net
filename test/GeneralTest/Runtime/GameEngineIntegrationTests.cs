using GalNet.Core.Graph;
using GalNet.Core.Scene;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Loader;
using GalNet.Runtime.SaveLoad;
using GalNet.Runtime.View;

namespace GeneralTest.Runtime.Engine;

public class GameEngineIntegrationTests
{
    [Test]
    public async Task Simple_Linear_Graph_Should_Run_To_Completion()
    {
        var graph = new Graph
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
                        new GenericComplexEntry { Id = 1, Type = "text",
                            Params = new() { ["speaker"] = "Narrator", ["content"] = "Test text" }
                        },
                        new GenericComplexEntry { Id = 2, Type = "jump",
                            Params = new() { ["type"] = "end" }
                        }
                    }
                }
            },
            Edges = { }
        };

        var view = new NullGameView(verbose: false);
        var engine = new GameEngine(graph, view);

        var finished = await engine.StepAsync();
        Assert.That(finished, Is.False);
    }

    [Test]
    public async Task Graph_With_Variable_Set_Then_Condition_Branch_Should_Take_Expected_Path()
    {
        var graph = new Graph
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
                        new GenericComplexEntry { Id = 1, Type = "variable",
                            Params = new() { ["action"] = "set", ["target"] = "flag_route_a", ["value"] = "true", ["type"] = "bool" }
                        }
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
                        new GenericComplexEntry { Id = 1, Type = "variable",
                            Params = new() { ["action"] = "set", ["target"] = "route_taken", ["value"] = "a", ["type"] = "string" }
                        },
                        new GenericComplexEntry { Id = 2, Type = "jump", Params = new() { ["type"] = "end" } }
                    }
                },
                new Group
                {
                    Id = "group_b",
                    Name = "RouteB",
                    Entries =
                    {
                        new GenericComplexEntry { Id = 1, Type = "variable",
                            Params = new() { ["action"] = "set", ["target"] = "route_taken", ["value"] = "b", ["type"] = "string" }
                        },
                        new GenericComplexEntry { Id = 2, Type = "jump", Params = new() { ["type"] = "end" } }
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

        var view = new NullGameView(verbose: false);
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
        var graph = new Graph
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
                        new GenericComplexEntry { Id = 1, Type = "text",
                            Params = new() { ["speaker"] = "Narrator", ["content"] = "Half way" }
                        },
                        new GenericComplexEntry { Id = 2, Type = "variable",
                            Params = new() { ["action"] = "set", ["target"] = "save_point", ["value"] = "true", ["type"] = "bool" }
                        },
                        new GenericComplexEntry { Id = 3, Type = "text",
                            Params = new() { ["speaker"] = "Narrator", ["content"] = "End" }
                        },
                        new GenericComplexEntry { Id = 4, Type = "jump", Params = new() { ["type"] = "end" } }
                    }
                }
            },
            Edges = { }
        };

        var view1 = new NullGameView(verbose: false);
        var engine1 = new GameEngine(graph, view1);
        await engine1.StepAsync();

        var saveData = engine1.CreateSaveData();
        var saveJson = SaveManager.Serialize(saveData);
        var restored = SaveManager.Deserialize(saveJson);

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Variables.GetValueOrDefault("save_point")!.AsBool(), Is.True);
    }
}
