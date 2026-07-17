using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Commands;

public sealed class EditorDocumentValidator
{
    public ValidationResult Validate(EditorProjectDocument document)
    {
        var diagnostics = new List<EditorDiagnostic>();
        AddDuplicateDiagnostics(document.Graph.Nodes.Select(node => node.Id), "graph.node.duplicateId", "node", diagnostics);
        AddDuplicateDiagnostics(document.Graph.Edges.Select(edge => edge.Id), "graph.edge.duplicateId", "edge", diagnostics);
        if (document.Graph.Nodes.Any(node => string.IsNullOrWhiteSpace(node.Id)))
            diagnostics.Add(EditorDiagnostic.Error("graph.node.idRequired", "Every graph node must have a stable ID."));
        if (document.Graph.Edges.Any(edge => string.IsNullOrWhiteSpace(edge.Id)))
            diagnostics.Add(EditorDiagnostic.Error("graph.edge.idRequired", "Every graph edge must have a stable ID."));

        var nodes = document.Graph.Nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(document.Graph.RootNodeId) && !nodes.ContainsKey(document.Graph.RootNodeId))
            diagnostics.Add(EditorDiagnostic.Error("graph.root.notFound", $"Root node '{document.Graph.RootNodeId}' does not exist.", "graph"));

        foreach (var edge in document.Graph.Edges)
        {
            if (!nodes.TryGetValue(edge.FromNodeId, out var from))
                diagnostics.Add(EditorDiagnostic.Error("graph.edge.fromNotFound", $"Edge '{edge.Id}' references missing source node '{edge.FromNodeId}'.", $"graph/edges/{edge.Id}"));
            if (!nodes.ContainsKey(edge.ToNodeId))
                diagnostics.Add(EditorDiagnostic.Error("graph.edge.toNotFound", $"Edge '{edge.Id}' references missing target node '{edge.ToNodeId}'.", $"graph/edges/{edge.Id}"));
            if (from is not null && (edge.FromOutlet < 0 || edge.FromOutlet >= OutputCount(from)))
                diagnostics.Add(EditorDiagnostic.Error("graph.edge.invalidOutlet", $"Edge '{edge.Id}' references invalid outlet {edge.FromOutlet} on '{from.Id}'.", $"graph/edges/{edge.Id}"));
        }

        var entryIds = document.GroupEntries.Values.SelectMany(entries => entries).Select(entry => entry.StableId).ToList();
        AddDuplicateDiagnostics(entryIds, "group.entry.duplicateId", "entry", diagnostics);
        if (entryIds.Any(string.IsNullOrWhiteSpace))
            diagnostics.Add(EditorDiagnostic.Error("group.entry.idRequired", "Every group entry must have a stable ID."));

        var optionIds = document.Graph.Nodes.SelectMany(node => node.Options ?? []).Select(option => option.Id).ToList();
        AddDuplicateDiagnostics(optionIds, "branch.option.duplicateId", "choice option", diagnostics);
        if (optionIds.Any(string.IsNullOrWhiteSpace))
            diagnostics.Add(EditorDiagnostic.Error("branch.option.idRequired", "Every choice option must have a stable ID."));
        var conditionIds = document.Graph.Nodes.SelectMany(node => node.Conditions ?? []).Select(condition => condition.Id).ToList();
        AddDuplicateDiagnostics(conditionIds, "branch.condition.duplicateId", "branch condition", diagnostics);
        if (conditionIds.Any(string.IsNullOrWhiteSpace))
            diagnostics.Add(EditorDiagnostic.Error("branch.condition.idRequired", "Every branch condition must have a stable ID."));

        var variables = document.Graph.PlayerVariables.Concat(document.Graph.SaveVariables).ToList();
        var variableNames = variables.Select(variable => variable.Name).ToList();
        AddDuplicateDiagnostics(variableNames, "variable.duplicateName", "variable", diagnostics);
        AddDuplicateDiagnostics(variables.Select(variable => variable.DefaultValue.Uid), "variable.duplicateUid", "variable UID", diagnostics);
        return new ValidationResult(diagnostics);
    }

    private static void AddDuplicateDiagnostics(
        IEnumerable<string> values,
        string code,
        string kind,
        ICollection<EditorDiagnostic> diagnostics)
    {
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)).GroupBy(value => value, StringComparer.Ordinal).Where(group => group.Count() > 1))
            diagnostics.Add(EditorDiagnostic.Error(code, $"Duplicate {kind} identifier '{value.Key}'."));
    }

    private static int OutputCount(EditorGraphNodeDto node)
    {
        if (node.BranchType?.Equals("Choice", StringComparison.OrdinalIgnoreCase) == true)
            return Math.Max(1, node.Options?.Count ?? 0);
        if (node.BranchType?.Equals("Condition", StringComparison.OrdinalIgnoreCase) == true)
            return Math.Max(1, node.Conditions?.Count ?? 0);
        return 1;
    }
}
