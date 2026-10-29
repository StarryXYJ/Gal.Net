using GalNet.Core.Variable;

namespace GalNet.Editor.Services;

/// <summary>Project-owned editing state, independent of a dock page or recycled list row.</summary>
public sealed class VariablePanelState
{
    public int ScopeIndex { get; set; }
    public string Search { get; set; } = "";
    public Dictionary<VariableScope, string> Selection { get; } = [];
    public Dictionary<(VariableScope Scope, string Uid), VariablePanelDraft> Drafts { get; } = [];
}

public sealed record VariablePanelDraft(ProjectVariableDefinition Baseline, string Name, VariableType Type, string Default, string Description);
