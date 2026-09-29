using GalNet.Core.Graph;
using GalNetGraph = GalNet.Core.Graph.Graph;

namespace GeneralTest.Graph;

public class GraphModelTests
{
    [Test]
    public void Group_Should_Have_Correct_NodeType()
    {
        var group = new Group { Name = "Opening" };
        Assert.That(group.NodeType, Is.EqualTo(NodeType.Group));
        Assert.That(group.Name, Is.EqualTo("Opening"));
        Assert.That(group.Id, Is.Not.Empty);
    }

    [Test]
    public void Branch_Should_Have_Correct_NodeType()
    {
        var branch = new Branch { BranchType = BranchType.Choice, Name = "Choice_01" };
        Assert.That(branch.NodeType, Is.EqualTo(NodeType.Branch));
        Assert.That(branch.BranchType, Is.EqualTo(BranchType.Choice));
    }

    [Test]
    public void Graph_Should_Have_Empty_Collections_By_Default()
    {
        var graph = new GalNetGraph();
        Assert.That(graph.Nodes, Is.Empty);
        Assert.That(graph.Edges, Is.Empty);
    }

    [Test]
    public void Graph_Should_Contain_Added_Nodes_And_Edges()
    {
        var graph = new GalNetGraph { Name = "MainGraph" };
        var group1 = new Group { Id = "g1" };
        var group2 = new Group { Id = "g2" };
        var edge = new Edge("g1", 0, "g2");

        graph.Nodes.Add(group1);
        graph.Nodes.Add(group2);
        graph.Edges.Add(edge);
        graph.RootNodeId = "g1";

        Assert.That(graph.Nodes, Has.Count.EqualTo(2));
        Assert.That(graph.Edges, Has.Count.EqualTo(1));
        Assert.That(graph.RootNodeId, Is.EqualTo("g1"));
    }

    [Test]
    public void Branch_Choice_Should_Have_Options()
    {
        var branch = new Branch
        {
            BranchType = BranchType.Choice,
            Options =
            {
                new BranchOption { Text = "Yes" },
                new BranchOption { Text = "No", Condition = "flag == true" }
            }
        };

        Assert.That(branch.Options, Has.Count.EqualTo(2));
        Assert.That(branch.Options[0].Text, Is.EqualTo("Yes"));
        Assert.That(branch.Options[0].Condition, Is.Empty);
        Assert.That(branch.Options[1].Condition, Is.EqualTo("flag == true"));
    }

    [Test]
    public void Branch_Condition_Should_Have_Conditions()
    {
        var branch = new Branch
        {
            BranchType = BranchType.Condition,
            Conditions =
            {
                new BranchCondition { Expression = "route == 'alice'" },
                new BranchCondition { Expression = "route == 'bob'" }
            }
        };

        Assert.That(branch.Conditions, Has.Count.EqualTo(2));
        Assert.That(branch.Conditions[0].Expression, Is.EqualTo("route == 'alice'"));
    }

    [Test]
    public void Edge_Should_Store_Connection_Info()
    {
        var edge = new Edge("node_a", 2, "node_b");
        Assert.That(edge.FromNodeId, Is.EqualTo("node_a"));
        Assert.That(edge.FromOutlet, Is.EqualTo(2));
        Assert.That(edge.ToNodeId, Is.EqualTo("node_b"));
    }
}
