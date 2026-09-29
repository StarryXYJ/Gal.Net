using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Services;

namespace GeneralTest.Editor;

[TestFixture]
public class GraphDocumentMapperTests
{
    [Test]
    public void Load_AddsMissingEntryAndPreservesTheReachableRoot()
    {
        var loaded = new LoadedEditorProjectDocument
        {
            Document = new EditorGraphDocument
            {
                RootNodeId = "group",
                Nodes = [new EditorGraphNodeDto { Id = "group", Type = "Group", Name = "Opening", X = 100, Y = 200 }]
            }
        };

        var graph = new GraphDocumentMapper().Load(loaded);

        Assert.That(graph.Nodes, Has.Count.EqualTo(2));
        Assert.That(graph.Nodes[0].IsEntryNode, Is.True);
        Assert.That(graph.Edges, Has.Count.EqualTo(1));
        Assert.That(graph.Edges[0].From, Is.SameAs(graph.Nodes[0]));
        Assert.That(graph.Edges[0].To.Id, Is.EqualTo("group"));
    }

    [Test]
    public void CreateDocument_RoundTripsBranchAndGroupData()
    {
        var mapper = new GraphDocumentMapper();
        var loaded = new LoadedEditorProjectDocument
        {
            Document = new EditorGraphDocument
            {
                Nodes =
                [
                    new EditorGraphNodeDto { Id = "entry", Type = "Entry", Name = "Entry" },
                    new EditorGraphNodeDto { Id = "group", Type = "Group", Name = "Opening" },
                    new EditorGraphNodeDto { Id = "choice", Type = "Branch", BranchType = "Choice", Name = "Choice", Options = [new EditorGraphBranchOptionDto { Text = "Continue" }] }
                ],
                Edges =
                [
                    new EditorGraphEdgeDto { FromNodeId = "entry", ToNodeId = "group" },
                    new EditorGraphEdgeDto { FromNodeId = "group", ToNodeId = "choice" }
                ]
            }
        };

        var graph = mapper.Load(loaded);
        var document = mapper.CreateDocument("Demo", 2, graph.Nodes, graph.Edges, [], []);

        Assert.That(document.RootNodeId, Is.EqualTo("entry"));
        Assert.That(document.Nodes.Single(node => node.Id == "choice").Options![0].Text, Is.EqualTo("Continue"));
        Assert.That(document.Edges, Has.Count.EqualTo(2));
    }
}
