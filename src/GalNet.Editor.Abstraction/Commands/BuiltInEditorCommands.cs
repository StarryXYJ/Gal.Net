using System.Text.Json;
using GalNet.Core.Variable;

namespace GalNet.Editor.Abstraction.Commands;

public enum EditorNodeKind
{
    Entry,
    LinearGroup,
    ChoiceBranch,
    ConditionBranch
}

public sealed record CreateNodeCommand(
    string NodeId,
    EditorNodeKind Kind,
    string Name,
    double X,
    double Y) : IProjectEditCommand
{
    public string CommandId => "graph.node.create";
}

public sealed record DeleteNodeCommand(string NodeId, bool DeleteGroupEntries = true) : IProjectEditCommand
{
    public string CommandId => "graph.node.delete";
}

public sealed record RenameNodeCommand(string NodeId, string Name) : IProjectEditCommand
{
    public string CommandId => "graph.node.rename";
}

public sealed record NodePositionChange(string NodeId, double X, double Y);

public sealed record MoveNodesCommand(IReadOnlyList<NodePositionChange> Nodes) : IProjectEditCommand
{
    public string CommandId => "graph.node.move";
}

public sealed record SetRootNodeCommand(string NodeId) : IProjectEditCommand
{
    public string CommandId => "graph.node.setRoot";
}

public sealed record ConnectNodesCommand(
    string FromNodeId,
    int Outlet,
    string ToNodeId,
    string? EdgeId = null) : IProjectEditCommand
{
    public string CommandId => "graph.edge.connect";
}

public sealed record DeleteEdgeCommand(
    string? EdgeId = null,
    string? FromNodeId = null,
    int? Outlet = null,
    string? ToNodeId = null) : IProjectEditCommand
{
    public string CommandId => "graph.edge.delete";
}

public sealed record AddEntryCommand(
    string GroupId,
    string EntryId,
    int? Index = null,
    string Type = "text",
    string Condition = "",
    IReadOnlyDictionary<string, string>? Parameters = null) : IProjectEditCommand
{
    public string CommandId => "group.entry.add";
}

public sealed record DeleteEntryCommand(string GroupId, string EntryId) : IProjectEditCommand
{
    public string CommandId => "group.entry.delete";
}

public sealed record MoveEntryCommand(string GroupId, string EntryId, int Index) : IProjectEditCommand
{
    public string CommandId => "group.entry.move";
}

public sealed record SetEntryTypeCommand(string GroupId, string EntryId, string Type) : IProjectEditCommand
{
    public string CommandId => "group.entry.setType";
}

public sealed record SetEntryConditionCommand(string GroupId, string EntryId, string Condition) : IProjectEditCommand
{
    public string CommandId => "group.entry.setCondition";
}

public sealed record SetEntryParametersCommand(
    string GroupId,
    string EntryId,
    IReadOnlyDictionary<string, string> Parameters) : IProjectEditCommand
{
    public string CommandId => "group.entry.setParameters";
}

public sealed record PatchEntryParametersCommand(
    string GroupId,
    string EntryId,
    IReadOnlyDictionary<string, string?> Parameters) : IProjectEditCommand
{
    public string CommandId => "group.entry.patchParameters";
}

public sealed record AddChoiceOptionCommand(
    string NodeId,
    string OptionId,
    int? Index = null,
    string Text = "",
    string Condition = "") : IProjectEditCommand
{
    public string CommandId => "branch.option.add";
}

public sealed record DeleteChoiceOptionCommand(string NodeId, string OptionId) : IProjectEditCommand
{
    public string CommandId => "branch.option.delete";
}

public sealed record MoveChoiceOptionCommand(string NodeId, string OptionId, int Index) : IProjectEditCommand
{
    public string CommandId => "branch.option.move";
}

public sealed record SetChoiceOptionTextCommand(string NodeId, string OptionId, string Text) : IProjectEditCommand
{
    public string CommandId => "branch.option.setText";
}

public sealed record SetChoiceOptionConditionCommand(string NodeId, string OptionId, string Condition) : IProjectEditCommand
{
    public string CommandId => "branch.option.setCondition";
}

public sealed record AddBranchConditionCommand(
    string NodeId,
    string ConditionId,
    int? Index = null,
    string Expression = "true") : IProjectEditCommand
{
    public string CommandId => "branch.condition.add";
}

public sealed record DeleteBranchConditionCommand(string NodeId, string ConditionId) : IProjectEditCommand
{
    public string CommandId => "branch.condition.delete";
}

public sealed record MoveBranchConditionCommand(string NodeId, string ConditionId, int Index) : IProjectEditCommand
{
    public string CommandId => "branch.condition.move";
}

public sealed record SetBranchConditionExpressionCommand(
    string NodeId,
    string ConditionId,
    string Expression) : IProjectEditCommand
{
    public string CommandId => "branch.condition.setExpression";
}

public sealed record AddVariableDefinitionCommand(
    VariableScope Scope,
    string Name,
    VariableType Type,
    JsonElement? DefaultValue = null,
    int? Index = null,
    string? Uid = null) : IProjectEditCommand
{
    public string CommandId => "variable.definition.add";
}

public sealed record DeleteVariableDefinitionCommand(VariableScope Scope, string Name) : IProjectEditCommand
{
    public string CommandId => "variable.definition.delete";
}

public sealed record MoveVariableDefinitionCommand(VariableScope Scope, string Name, int Index) : IProjectEditCommand
{
    public string CommandId => "variable.definition.move";
}

public sealed record RenameVariableDefinitionCommand(
    VariableScope Scope,
    string Name,
    string NewName,
    bool UpdateReferences = false) : IProjectEditCommand
{
    public string CommandId => "variable.definition.rename";
}

public sealed record SetVariableDefinitionTypeCommand(
    VariableScope Scope,
    string Name,
    VariableType Type) : IProjectEditCommand
{
    public string CommandId => "variable.definition.setType";
}

public sealed record SetVariableDefaultValueCommand(
    VariableScope Scope,
    string Name,
    JsonElement Value) : IProjectEditCommand
{
    public string CommandId => "variable.definition.setDefault";
}

public sealed record RenameProjectCommand(string Name) : IProjectEditCommand
{
    public string CommandId => "project.rename";
}

public sealed record PatchProjectSettingsCommand(IReadOnlyDictionary<string, JsonElement> Values) : IProjectEditCommand
{
    public string CommandId => "project.settings.patch";
}

public sealed record ApplyUiPresetCommand(
    GalNet.Core.UI.UiPageKind Page,
    string PresetId,
    IReadOnlyDictionary<string, string>? Defaults = null) : IProjectEditCommand
{
    public string CommandId => "ui.preset.apply";
}

public sealed record SetUiProjectValueCommand(
    GalNet.Core.UI.UiPageKind Page,
    string Key,
    string Value) : IProjectEditCommand
{
    public string CommandId => "ui.value.set";
}

public sealed record PatchUiProjectValuesCommand(
    GalNet.Core.UI.UiPageKind Page,
    IReadOnlyDictionary<string, string?> Values) : IProjectEditCommand
{
    public string CommandId => "ui.values.patch";
}

public sealed record ResetUiProjectValuesCommand(
    GalNet.Core.UI.UiPageKind Page,
    IReadOnlyList<string> Keys,
    IReadOnlyDictionary<string, string>? Defaults = null) : IProjectEditCommand
{
    public string CommandId => "ui.values.reset";
}

public sealed record ApplyUiColorPaletteCommand(string PaletteId) : IProjectEditCommand
{
    public string CommandId => "ui.palette.apply";
}

public sealed record ReplaceUiProjectCommand(GalNet.Core.UI.UiProject Project) : IProjectEditCommand
{
    public string CommandId => "ui.project.replace";
}
