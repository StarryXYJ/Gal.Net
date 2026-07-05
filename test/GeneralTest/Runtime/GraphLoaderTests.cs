using GalNet.Core.Graph;
using GalNet.Runtime.Loader;

namespace GeneralTest.Runtime;

public class GraphLoaderTests
{
    [Test]
    public void LoadFromJson_Should_Parse_Graph()
    {
        var json = """
        {
          "version": 1,
          "name": "TestGraph",
          "rootNodeId": "g1",
          "nodes": [
            { "id": "g1", "type": "Group", "name": "Intro" },
            { "id": "b1", "type": "Branch", "name": "Choice", "branchType": "Choice",
              "options": [{ "text": "Yes" }, { "text": "No", "condition": "flag" }]
            }
          ],
          "edges": [
            { "fromNodeId": "g1", "fromOutlet": 0, "toNodeId": "b1" }
          ]
        }
        """;

        var graph = GraphLoader.LoadFromJson(json);

        Assert.That(graph.Name, Is.EqualTo("TestGraph"));
        Assert.That(graph.RootNodeId, Is.EqualTo("g1"));
        Assert.That(graph.Nodes, Has.Count.EqualTo(2));
        Assert.That(graph.Edges, Has.Count.EqualTo(1));

        var group = graph.Nodes.OfType<Group>().First();
        Assert.That(group.Name, Is.EqualTo("Intro"));

        var branch = graph.Nodes.OfType<Branch>().First();
        Assert.That(branch.BranchType, Is.EqualTo(BranchType.Choice));
        Assert.That(branch.Options, Has.Count.EqualTo(2));
        Assert.That(branch.Options[1].Condition, Is.EqualTo("flag"));
    }

    [Test]
    public void LoadFromJson_Should_Parse_Condition_Branch()
    {
        var json = """
        {
          "name": "Test", "rootNodeId": "b1",
          "nodes": [
            { "id": "b1", "type": "Branch", "branchType": "Condition",
              "conditions": [
                { "expression": "route == 'a'" },
                { "expression": "route == 'b'" }
              ]
            }
          ],
          "edges": []
        }
        """;

        var graph = GraphLoader.LoadFromJson(json);
        var branch = graph.Nodes.OfType<Branch>().First();

        Assert.That(branch.BranchType, Is.EqualTo(BranchType.Condition));
        Assert.That(branch.Conditions, Has.Count.EqualTo(2));
    }

    [Test]
    public void LoadFromJson_Edge_Should_Store_Data()
    {
        var json = """
        { "name": "T", "rootNodeId": "", "nodes": [], "edges": [
          { "fromNodeId": "a", "fromOutlet": 2, "toNodeId": "b" }
        ]}
        """;

        var graph = GraphLoader.LoadFromJson(json);
        Assert.That(graph.Edges[0].FromNodeId, Is.EqualTo("a"));
        Assert.That(graph.Edges[0].FromOutlet, Is.EqualTo(2));
        Assert.That(graph.Edges[0].ToNodeId, Is.EqualTo("b"));
    }
}
