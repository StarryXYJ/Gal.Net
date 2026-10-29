using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Commands;

/// <summary>Shared graph invariants; GUI history and disk command execution remain separate adapters.</summary>
public static class GraphEditRules
{
    public static bool IsFinitePosition(double x, double y) => double.IsFinite(x) && double.IsFinite(y);
    public static string NormalizeName(string name) => name.Trim();
    public static int OutputCount(EditorGraphNodeDto node) => node.BranchType switch
    {
        "Choice" => node.Options?.Count ?? 0,
        "Condition" => node.Conditions?.Count ?? 0,
        _ => 1
    };
    public static EditorDiagnostic? ConnectionError(EditorGraphNodeDto from, int outlet, EditorGraphNodeDto? to)
    {
        if (from.Id == to?.Id) return EditorDiagnostic.Error("graph.edge.selfConnection", "A node cannot connect to itself.");
        if (to?.Type.Equals("Entry", StringComparison.OrdinalIgnoreCase) == true)
            return EditorDiagnostic.Error("graph.edge.entryInput", "The entry node cannot have an incoming edge.", $"graph/nodes/{to.Id}");
        return outlet < 0 || outlet >= OutputCount(from)
            ? EditorDiagnostic.Error("graph.edge.invalidOutlet", $"Outlet {outlet} does not exist on node '{from.Id}'.", $"graph/nodes/{from.Id}") : null;
    }
}
