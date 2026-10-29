using System.Text.Json;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Shared.Tests;

public sealed class GraphValidationConsistencyTests
{
    [TestCase("Choice")]
    [TestCase("Condition")]
    public void EmptyBranchHasNoConnectableOutletInCommandsOrDocumentValidation(string branchType)
    {
        var from = new EditorGraphNodeDto { Id = "branch", Type = "Branch", BranchType = branchType };
        var to = new EditorGraphNodeDto { Id = "target", Type = "Group" };
        var graph = new EditorGraphDocument
        {
            RootNodeId = from.Id,
            Nodes = [from, to],
            Edges = [new() { Id = "edge", FromNodeId = from.Id, ToNodeId = to.Id, FromOutlet = 0 }]
        };
        Assert.That(GraphEditRules.ConnectionError(from, 0, to)?.Code, Is.EqualTo("graph.edge.invalidOutlet"));
        Assert.That(EditorDocumentValidator.Validate(new() { Graph = graph }).Diagnostics.Select(item => item.Code),
            Does.Contain("graph.edge.invalidOutlet"));
    }

    [TestCase("a", "a", "Group", "graph.edge.selfConnection")]
    [TestCase("a", "b", "Entry", "graph.edge.entryInput")]
    public void RawGraphReportsSharedConnectionRuleOnce(string from, string to, string targetType, string code)
    {
        var nodes = new List<object> { new { id = "a", name = "A", type = "Entry" } };
        if (to != from) nodes.Add(new { id = "b", name = "B", type = targetType });
        var raw = JsonSerializer.SerializeToElement(new
        {
            version = 2,
            name = "Graph",
            rootNodeId = "a",
            nodes,
            edges = new[] { new { id = "edge", fromNodeId = from, toNodeId = to, fromOutlet = 0 } },
            playerVariables = Array.Empty<object>(),
            saveVariables = Array.Empty<object>()
        });
        var diagnostics = new List<ToolDiagnostic>();
        new ProjectValidationService(Path.GetTempPath()).ValidateGraph(raw, diagnostics, checkSourceReferences: false);
        Assert.That(diagnostics.Count(item => item.Code == code), Is.EqualTo(1));
        Assert.That(diagnostics.Single(item => item.Code == code).JsonPointer, Is.EqualTo("/edges/0"));
    }
}
