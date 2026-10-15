using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Services;

namespace GeneralTest.Editor;

[TestFixture]
public class GraphDocumentMapperTests
{
    [Test]
    public void Load_RejectsMissingEntryWithoutMutatingTheAuthorDocument()
    {
        var loaded = new LoadedEditorProjectDocument
        {
            Document = new EditorGraphDocument
            {
                RootNodeId = "group",
                Nodes = [new EditorGraphNodeDto { Id = "group", Type = "Group", Name = "Opening", X = 100, Y = 200 }]
            }
        };

        Assert.That(() => GraphDocumentMapper.Load(loaded), Throws.TypeOf<InvalidDataException>());
        Assert.That(loaded.Document.Nodes, Has.Count.EqualTo(1));
        Assert.That(loaded.Document.RootNodeId, Is.EqualTo("group"));
    }

    [Test]
    public void CreateDocument_RoundTripsBranchAndGroupData()
    {
        var loaded = new LoadedEditorProjectDocument
        {
            Document = new EditorGraphDocument
            {
                RootNodeId = "entry",
                Nodes =
                [
                    new EditorGraphNodeDto { Id = "entry", Type = "Entry", Name = "Entry" },
                    new EditorGraphNodeDto { Id = "group", Type = "Group", Name = "Opening", File = "groups/nested/intro.rawgalgroup" },
                    new EditorGraphNodeDto { Id = "choice", Type = "Branch", BranchType = "Choice", Name = "Choice", Options = [new EditorGraphBranchOptionDto { Text = "Continue" }] }
                ],
                Edges =
                [
                    new EditorGraphEdgeDto { FromNodeId = "entry", ToNodeId = "group" },
                    new EditorGraphEdgeDto { FromNodeId = "group", ToNodeId = "choice" }
                ]
            }
        };

        var graph = GraphDocumentMapper.Load(loaded);
        var document = GraphDocumentMapper.CreateDocument("Demo", 2, graph.Nodes, graph.Edges, [], []);

        Assert.That(document.RootNodeId, Is.EqualTo("entry"));
        Assert.That(document.Nodes.Single(node => node.Id == "group").File, Is.EqualTo("groups/nested/intro.rawgalgroup"));
        Assert.That(document.Nodes[0], Is.SameAs(loaded.Document.Nodes[0]));
        Assert.That(graph.Edges[0].Source, Is.SameAs(loaded.Document.Edges[0]));
        Assert.That(document.Nodes.Single(node => node.Id == "choice").Options![0].Text, Is.EqualTo("Continue"));
        Assert.That(document.Edges, Has.Count.EqualTo(2));
    }
}
