using System.Text.Json;
using System.Text.RegularExpressions;
using GalNet.Core.I18n;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Shared.Commands;

public sealed partial class BuiltInEditorCommandHandler : IEditorCommandHandler
{
    private static readonly HashSet<string> AllowedUiSettingKeys = new(StringComparer.Ordinal)
    {
        "backgroundImage", "backgroundColor", "backgroundStretch", "contentPadding",
        "titleColor", "titleFontSize", "menuTextColor", "menuHoverTextColor", "menuFontSize",
        "titleMenuGap", "menuSpacing", "showGallery", "showAbout", "menuItemBackgroundColor",
        "menuItemHoverBackgroundColor", "menuItemWidth", "menuItemHeight",
        "dialogueBackgroundColor", "dialogueBackgroundImage", "dialogueBackgroundImageOpacity",
        "dialogueTextColor", "speakerTextColor", "dialogueHeight", "dialogueMargin",
        "dialogueCornerRadius", "dialogueFontSize", "choiceLayout", "choiceButtonColor",
        "choiceButtonTextColor", "choiceButtonWidth", "choiceButtonHeight", "choiceSpacing",
        "commandBarVisible", "commandTextColor", "commandHoverTextColor", "commandSelectedTextColor",
        "panelColor", "textColor", "buttonColor", "buttonTextColor", "backButtonForegroundColor",
        "sliderTrackColor", "sliderFillColor", "sliderThumbColor", "sliderThumbBorderColor",
        "checkBoxBorderColor", "checkBoxFillColor", "checkBoxCheckColor", "contentAsset",
        "fontSize", "headingColor", "selectionColor", "linkColor", "linkHoverColor",
        "linkVisitedColor", "blockquoteBackgroundColor", "blockquoteBorderColor",
        "codeBackgroundColor", "codeBorderColor", "codeTextColor", "codeFontSize", "ruleColor"
    };
    public bool CanHandle(IProjectEditCommand command) => Domains.Any(domain => domain.CanHandle(command));

    public CommandExecution Execute(
        EditorProjectDocument document,
        IProjectEditCommand command,
        EditorCommandContext context) => Domains.FirstOrDefault(domain => domain.CanHandle(command))?.Execute(document, command, context)
            ?? CommandExecution.Failed(EditorDiagnostic.Error("command.unsupported", $"Unsupported command '{command.CommandId}'."));

    private static CommandExecution CreateNode(EditorProjectDocument document, CreateNodeCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.NodeId))
            return Error("graph.node.idRequired", "Node ID is required.");
        if (FindNode(document, command.NodeId) is not null)
            return Error("graph.node.duplicateId", $"Node '{command.NodeId}' already exists.", NodeResource(command.NodeId));
        if (!double.IsFinite(command.X) || !double.IsFinite(command.Y))
            return Error("graph.node.invalidPosition", "Node coordinates must be finite numbers.");
        if (command.Kind == EditorNodeKind.Entry && document.Graph.Nodes.Any(IsEntryNode))
            return Error("graph.node.entryExists", "The graph already contains an entry node.");

        var node = new EditorGraphNodeDto
        {
            Id = command.NodeId,
            Type = command.Kind switch
            {
                EditorNodeKind.Entry => "Entry",
                EditorNodeKind.LinearGroup => "Group",
                _ => "Branch"
            },
            BranchType = command.Kind switch
            {
                EditorNodeKind.ChoiceBranch => "Choice",
                EditorNodeKind.ConditionBranch => "Condition",
                _ => null
            },
            Name = string.IsNullOrWhiteSpace(command.Name) ? command.NodeId : command.Name.Trim(),
            X = command.X,
            Y = command.Y,
            File = command.Kind == EditorNodeKind.LinearGroup ? $"groups/{command.NodeId}.galgroup" : null,
            Options = command.Kind == EditorNodeKind.ChoiceBranch ? [] : null,
            Conditions = command.Kind == EditorNodeKind.ConditionBranch ? [] : null
        };
        document.Graph.Nodes.Add(node);
        if (command.Kind == EditorNodeKind.LinearGroup)
            document.GroupEntries[command.NodeId] = [];
        if (command.Kind == EditorNodeKind.Entry || string.IsNullOrWhiteSpace(document.Graph.RootNodeId))
            document.Graph.RootNodeId = command.NodeId;

        return Success($"Created node '{node.Name}'.", "History.Graph.CreateNode", NodeResource(node.Id), node.Name);
    }

    private static CommandExecution DeleteNode(EditorProjectDocument document, DeleteNodeCommand command)
    {
        var node = FindNode(document, command.NodeId);
        if (node is null)
            return NotFound("graph.node.notFound", "Node", command.NodeId, NodeResource(command.NodeId));
        if (IsEntryNode(node))
            return Error("graph.node.entryDeleteDenied", "The entry node cannot be deleted.", NodeResource(command.NodeId));

        var edgeCount = document.Graph.Edges.RemoveAll(edge =>
            edge.FromNodeId == node.Id || edge.ToNodeId == node.Id);
        document.Graph.Nodes.Remove(node);
        if (command.DeleteGroupEntries)
            document.GroupEntries.Remove(node.Id);
        if (document.Graph.RootNodeId == node.Id)
            document.Graph.RootNodeId = document.Graph.Nodes.FirstOrDefault(IsEntryNode)?.Id
                ?? document.Graph.Nodes.FirstOrDefault()?.Id ?? "";

        return Success(
            $"Deleted node '{node.Name}' and {edgeCount} connected edge(s).",
            "History.Graph.DeleteNode",
            NodeResource(node.Id),
            node.Name);
    }

    private static CommandExecution RenameNode(EditorProjectDocument document, RenameNodeCommand command)
    {
        var node = FindNode(document, command.NodeId);
        if (node is null)
            return NotFound("graph.node.notFound", "Node", command.NodeId, NodeResource(command.NodeId));
        if (string.IsNullOrWhiteSpace(command.Name))
            return Error("graph.node.nameRequired", "Node name is required.", NodeResource(command.NodeId));
        var oldName = node.Name;
        node.Name = command.Name.Trim();
        return Success($"Renamed node '{oldName}' to '{node.Name}'.", "History.Graph.RenameNode", NodeResource(node.Id), oldName, node.Name);
    }

    private static CommandExecution MoveNodes(EditorProjectDocument document, MoveNodesCommand command)
    {
        if (command.Nodes.Count == 0)
            return Error("graph.node.emptyMove", "At least one node position is required.");
        if (command.Nodes.Select(item => item.NodeId).Distinct(StringComparer.Ordinal).Count() != command.Nodes.Count)
            return Error("graph.node.duplicateMove", "A node can only appear once in a move command.");

        foreach (var position in command.Nodes)
        {
            var node = FindNode(document, position.NodeId);
            if (node is null)
                return NotFound("graph.node.notFound", "Node", position.NodeId, NodeResource(position.NodeId));
            if (!double.IsFinite(position.X) || !double.IsFinite(position.Y))
                return Error("graph.node.invalidPosition", $"Coordinates for node '{position.NodeId}' must be finite.");
        }
        foreach (var position in command.Nodes)
        {
            var node = FindNode(document, position.NodeId)!;
            node.X = position.X;
            node.Y = position.Y;
        }
        return Success(
            $"Moved {command.Nodes.Count} node(s).",
            "History.Graph.MoveNodes",
            command.Nodes.Select(item => NodeResource(item.NodeId)).ToArray(),
            command.Nodes.Count);
    }

    private static CommandExecution SetRoot(EditorProjectDocument document, SetRootNodeCommand command)
    {
        if (FindNode(document, command.NodeId) is null)
            return NotFound("graph.node.notFound", "Node", command.NodeId, NodeResource(command.NodeId));
        document.Graph.RootNodeId = command.NodeId;
        return Success($"Set graph root node to '{command.NodeId}'.", "History.Graph.SetRoot", NodeResource(command.NodeId), command.NodeId);
    }

    private static CommandExecution Connect(EditorProjectDocument document, ConnectNodesCommand command)
    {
        var from = FindNode(document, command.FromNodeId);
        var to = FindNode(document, command.ToNodeId);
        if (from is null)
            return NotFound("graph.node.notFound", "Node", command.FromNodeId, NodeResource(command.FromNodeId));
        if (to is null)
            return NotFound("graph.node.notFound", "Node", command.ToNodeId, NodeResource(command.ToNodeId));
        if (from.Id == to.Id)
            return Error("graph.edge.selfConnection", "A node cannot connect to itself.");
        if (IsEntryNode(to))
            return Error("graph.edge.entryInput", "The entry node cannot have an incoming edge.", NodeResource(to.Id));
        if (command.Outlet < 0 || command.Outlet >= OutputCount(from))
            return Error("graph.edge.invalidOutlet", $"Outlet {command.Outlet} does not exist on node '{from.Id}'.", NodeResource(from.Id));

        document.Graph.Edges.RemoveAll(edge =>
            edge.ToNodeId == to.Id ||
            (edge.FromNodeId == from.Id && edge.FromOutlet == command.Outlet));
        var edge = new EditorGraphEdgeDto
        {
            Id = string.IsNullOrWhiteSpace(command.EdgeId) ? Guid.NewGuid().ToString("N") : command.EdgeId,
            FromNodeId = from.Id,
            FromOutlet = command.Outlet,
            ToNodeId = to.Id
        };
        if (document.Graph.Edges.Any(item => item.Id == edge.Id))
            return Error("graph.edge.duplicateId", $"Edge '{edge.Id}' already exists.");
        document.Graph.Edges.Add(edge);
        return Success(
            $"Connected '{from.Id}' outlet {command.Outlet} to '{to.Id}'.",
            "History.Graph.ConnectNodes",
            EdgeResource(edge.Id),
            from.Name,
            to.Name);
    }

    private static CommandExecution DeleteEdge(EditorProjectDocument document, DeleteEdgeCommand command)
    {
        var edge = !string.IsNullOrWhiteSpace(command.EdgeId)
            ? document.Graph.Edges.FirstOrDefault(item => item.Id == command.EdgeId)
            : document.Graph.Edges.FirstOrDefault(item =>
                item.FromNodeId == command.FromNodeId &&
                item.FromOutlet == command.Outlet &&
                item.ToNodeId == command.ToNodeId);
        if (edge is null)
            return Error("graph.edge.notFound", "The requested graph edge does not exist.");
        document.Graph.Edges.Remove(edge);
        return Success(
            $"Deleted edge from '{edge.FromNodeId}' outlet {edge.FromOutlet} to '{edge.ToNodeId}'.",
            "History.Graph.DeleteEdge",
            EdgeResource(edge.Id),
            edge.FromNodeId,
            edge.ToNodeId);
    }

    private static CommandExecution AddEntry(EditorProjectDocument document, AddEntryCommand command)
    {
        if (!TryGetGroup(document, command.GroupId, out var entries, out var failure))
            return failure!;
        if (string.IsNullOrWhiteSpace(command.EntryId))
            return Error("group.entry.idRequired", "Entry ID is required.");
        if (document.GroupEntries.Values.SelectMany(value => value).Any(entry => entry.StableId == command.EntryId))
            return Error("group.entry.duplicateId", $"Entry '{command.EntryId}' already exists.");
        var groupEntries = entries!;
        var index = command.Index ?? groupEntries.Count;
        if (index < 0 || index > groupEntries.Count)
            return InvalidIndex("entry", index, groupEntries.Count);
        var entry = new EditorEntryData
        {
            StableId = command.EntryId,
            Type = string.IsNullOrWhiteSpace(command.Type) ? "text" : command.Type.Trim(),
            Condition = command.Condition ?? "",
            Parameters = SerializeParameters(command.Parameters ?? new Dictionary<string, string>())
        };
        groupEntries.Insert(index, entry);
        Renumber(groupEntries);
        return Success($"Added entry '{entry.StableId}' to group '{command.GroupId}'.", "History.Entry.Add", EntryResource(command.GroupId, entry.StableId), entry.StableId);
    }

    private static CommandExecution DeleteEntry(EditorProjectDocument document, DeleteEntryCommand command)
    {
        if (!TryFindEntry(document, command.GroupId, command.EntryId, out var entries, out var entry, out var failure))
            return failure!;
        entries!.Remove(entry!);
        Renumber(entries);
        return Success($"Deleted entry '{command.EntryId}' from group '{command.GroupId}'.", "History.Entry.Delete", EntryResource(command.GroupId, command.EntryId), command.EntryId);
    }

    private static CommandExecution MoveEntry(EditorProjectDocument document, MoveEntryCommand command)
    {
        if (!TryFindEntry(document, command.GroupId, command.EntryId, out var entries, out var entry, out var failure))
            return failure!;
        var groupEntries = entries!;
        if (command.Index < 0 || command.Index >= groupEntries.Count)
            return InvalidIndex("entry", command.Index, groupEntries.Count);
        var oldIndex = groupEntries.IndexOf(entry!);
        groupEntries.RemoveAt(oldIndex);
        groupEntries.Insert(command.Index, entry!);
        Renumber(groupEntries);
        return Success($"Moved entry '{command.EntryId}' to index {command.Index}.", "History.Entry.Move", EntryResource(command.GroupId, command.EntryId), command.EntryId, command.Index);
    }

    private static CommandExecution SetEntryType(EditorProjectDocument document, SetEntryTypeCommand command)
    {
        if (!TryFindEntry(document, command.GroupId, command.EntryId, out _, out var entry, out var failure))
            return failure!;
        if (string.IsNullOrWhiteSpace(command.Type))
            return Error("group.entry.typeRequired", "Entry type is required.");
        entry!.Type = command.Type.Trim();
        return Success($"Set entry '{command.EntryId}' type to '{entry.Type}'.", "History.Entry.SetType", EntryResource(command.GroupId, command.EntryId), command.EntryId, entry.Type);
    }

    private static CommandExecution SetEntryCondition(EditorProjectDocument document, SetEntryConditionCommand command)
    {
        if (!TryFindEntry(document, command.GroupId, command.EntryId, out _, out var entry, out var failure))
            return failure!;
        entry!.Condition = command.Condition ?? "";
        return Success($"Updated condition for entry '{command.EntryId}'.", "History.Entry.SetCondition", EntryResource(command.GroupId, command.EntryId), command.EntryId);
    }

    private static CommandExecution SetEntryParameters(EditorProjectDocument document, SetEntryParametersCommand command)
    {
        if (!TryFindEntry(document, command.GroupId, command.EntryId, out _, out var entry, out var failure))
            return failure!;
        entry!.Parameters = SerializeParameters(command.Parameters);
        return Success($"Replaced parameters for entry '{command.EntryId}'.", "History.Entry.SetParameters", EntryResource(command.GroupId, command.EntryId), command.EntryId);
    }

    private static CommandExecution PatchEntryParameters(EditorProjectDocument document, PatchEntryParametersCommand command)
    {
        if (!TryFindEntry(document, command.GroupId, command.EntryId, out _, out var entry, out var failure))
            return failure!;
        var parameters = ParseParameters(entry!.Parameters);
        foreach (var (key, value) in command.Parameters)
        {
            if (string.IsNullOrWhiteSpace(key))
                return Error("group.entry.parameterKeyRequired", "Entry parameter keys cannot be empty.");
            if (value is null) parameters.Remove(key);
            else parameters[key] = value;
        }
        entry.Parameters = SerializeParameters(parameters);
        return Success($"Patched parameters for entry '{command.EntryId}'.", "History.Entry.PatchParameters", EntryResource(command.GroupId, command.EntryId), command.EntryId);
    }

    private static CommandExecution AddOption(EditorProjectDocument document, AddChoiceOptionCommand command)
    {
        if (!TryGetChoiceNode(document, command.NodeId, out var node, out var failure))
            return failure!;
        if (string.IsNullOrWhiteSpace(command.OptionId))
            return Error("branch.option.idRequired", "Choice option ID is required.");
        if (document.Graph.Nodes.SelectMany(item => item.Options ?? []).Any(item => item.Id == command.OptionId))
            return Error("branch.option.duplicateId", $"Choice option '{command.OptionId}' already exists.");
        var options = node!.Options!;
        var index = command.Index ?? options.Count;
        if (index < 0 || index > options.Count)
            return InvalidIndex("choice option", index, options.Count);
        ShiftOutletsForInsert(document, node.Id, index);
        options.Insert(index, new EditorGraphBranchOptionDto { Id = command.OptionId, Text = command.Text ?? "", Condition = command.Condition ?? "" });
        return Success($"Added choice option '{command.OptionId}' to node '{node.Id}'.", "History.BranchOption.Add", OptionResource(node.Id, command.OptionId), command.OptionId);
    }

    private static CommandExecution DeleteOption(EditorProjectDocument document, DeleteChoiceOptionCommand command)
    {
        if (!TryFindOption(document, command.NodeId, command.OptionId, out var node, out var option, out var failure))
            return failure!;
        var index = node!.Options!.IndexOf(option!);
        node.Options.RemoveAt(index);
        RemoveOutletAndShift(document, node.Id, index);
        return Success($"Deleted choice option '{command.OptionId}'.", "History.BranchOption.Delete", OptionResource(node.Id, command.OptionId), command.OptionId);
    }

    private static CommandExecution MoveOption(EditorProjectDocument document, MoveChoiceOptionCommand command)
    {
        if (!TryFindOption(document, command.NodeId, command.OptionId, out var node, out var option, out var failure))
            return failure!;
        var choiceNode = node!;
        var options = choiceNode.Options!;
        if (command.Index < 0 || command.Index >= options.Count)
            return InvalidIndex("choice option", command.Index, options.Count);
        var oldIndex = options.IndexOf(option!);
        options.RemoveAt(oldIndex);
        options.Insert(command.Index, option!);
        RemapMovedOutlet(document, choiceNode.Id, oldIndex, command.Index);
        return Success($"Moved choice option '{command.OptionId}' to index {command.Index}.", "History.BranchOption.Move", OptionResource(choiceNode.Id, command.OptionId), command.OptionId, command.Index);
    }

    private static CommandExecution SetOptionText(EditorProjectDocument document, SetChoiceOptionTextCommand command)
    {
        if (!TryFindOption(document, command.NodeId, command.OptionId, out var node, out var option, out var failure))
            return failure!;
        option!.Text = command.Text ?? "";
        return Success($"Updated text for choice option '{command.OptionId}'.", "History.BranchOption.SetText", OptionResource(node!.Id, command.OptionId), command.OptionId);
    }

    private static CommandExecution SetOptionCondition(EditorProjectDocument document, SetChoiceOptionConditionCommand command)
    {
        if (!TryFindOption(document, command.NodeId, command.OptionId, out var node, out var option, out var failure))
            return failure!;
        option!.Condition = command.Condition ?? "";
        return Success($"Updated condition for choice option '{command.OptionId}'.", "History.BranchOption.SetCondition", OptionResource(node!.Id, command.OptionId), command.OptionId);
    }

    private static CommandExecution AddCondition(EditorProjectDocument document, AddBranchConditionCommand command)
    {
        if (!TryGetConditionNode(document, command.NodeId, out var node, out var failure))
            return failure!;
        if (string.IsNullOrWhiteSpace(command.ConditionId))
            return Error("branch.condition.idRequired", "Branch condition ID is required.");
        if (document.Graph.Nodes.SelectMany(item => item.Conditions ?? []).Any(item => item.Id == command.ConditionId))
            return Error("branch.condition.duplicateId", $"Branch condition '{command.ConditionId}' already exists.");
        var conditions = node!.Conditions!;
        var index = command.Index ?? conditions.Count;
        if (index < 0 || index > conditions.Count)
            return InvalidIndex("branch condition", index, conditions.Count);
        ShiftOutletsForInsert(document, node.Id, index);
        conditions.Insert(index, new EditorGraphBranchConditionDto { Id = command.ConditionId, Expression = command.Expression ?? "true" });
        return Success($"Added branch condition '{command.ConditionId}' to node '{node.Id}'.", "History.BranchCondition.Add", ConditionResource(node.Id, command.ConditionId), command.ConditionId);
    }

    private static CommandExecution DeleteCondition(EditorProjectDocument document, DeleteBranchConditionCommand command)
    {
        if (!TryFindCondition(document, command.NodeId, command.ConditionId, out var node, out var condition, out var failure))
            return failure!;
        var index = node!.Conditions!.IndexOf(condition!);
        node.Conditions.RemoveAt(index);
        RemoveOutletAndShift(document, node.Id, index);
        return Success($"Deleted branch condition '{command.ConditionId}'.", "History.BranchCondition.Delete", ConditionResource(node.Id, command.ConditionId), command.ConditionId);
    }

    private static CommandExecution MoveCondition(EditorProjectDocument document, MoveBranchConditionCommand command)
    {
        if (!TryFindCondition(document, command.NodeId, command.ConditionId, out var node, out var condition, out var failure))
            return failure!;
        var conditionNode = node!;
        var conditions = conditionNode.Conditions!;
        if (command.Index < 0 || command.Index >= conditions.Count)
            return InvalidIndex("branch condition", command.Index, conditions.Count);
        var oldIndex = conditions.IndexOf(condition!);
        conditions.RemoveAt(oldIndex);
        conditions.Insert(command.Index, condition!);
        RemapMovedOutlet(document, conditionNode.Id, oldIndex, command.Index);
        return Success($"Moved branch condition '{command.ConditionId}' to index {command.Index}.", "History.BranchCondition.Move", ConditionResource(conditionNode.Id, command.ConditionId), command.ConditionId, command.Index);
    }

    private static CommandExecution SetConditionExpression(EditorProjectDocument document, SetBranchConditionExpressionCommand command)
    {
        if (!TryFindCondition(document, command.NodeId, command.ConditionId, out var node, out var condition, out var failure))
            return failure!;
        condition!.Expression = command.Expression ?? "";
        return Success($"Updated expression for branch condition '{command.ConditionId}'.", "History.BranchCondition.SetExpression", ConditionResource(node!.Id, command.ConditionId), command.ConditionId);
    }

    private static CommandExecution AddVariable(EditorProjectDocument document, AddVariableDefinitionCommand command)
    {
        if (!IsValidVariableName(command.Name))
            return Error("variable.invalidName", $"Variable name '{command.Name}' is invalid. Use ASCII letters, digits, and underscores.");
        if (AllVariables(document).Any(item => item.Name == command.Name))
            return Error("variable.duplicateName", $"Variable '{command.Name}' already exists.");
        if (!string.IsNullOrWhiteSpace(command.Uid) && AllVariables(document).Any(item => item.DefaultValue.Uid == command.Uid))
            return Error("variable.duplicateUid", $"Variable UID '{command.Uid}' already exists.");
        var list = Variables(document, command.Scope);
        var index = command.Index ?? list.Count;
        if (index < 0 || index > list.Count)
            return InvalidIndex("variable", index, list.Count);
        var variable = new ProjectVariableDefinition
        {
            Name = command.Name,
            DefaultValue = new Variable
            {
                Uid = string.IsNullOrWhiteSpace(command.Uid) ? Guid.NewGuid().ToString("N") : command.Uid,
                Name = command.Name,
                Value = VariableValue.From("")
            }
        };
        variable.Type = command.Type;
        variable.DefaultValue.Name = command.Name;
        if (command.DefaultValue is { } value && !TrySetVariableValue(variable, value, out var message))
            return Error("variable.invalidDefault", message!);
        list.Insert(index, variable);
        SyncSettingsVariables(document);
        return Success($"Added {command.Scope.ToString().ToLowerInvariant()} variable '{command.Name}'.", "History.Variable.Add", VariableResource(command.Scope, command.Name), command.Name);
    }

    private static CommandExecution DeleteVariable(EditorProjectDocument document, DeleteVariableDefinitionCommand command)
    {
        var list = Variables(document, command.Scope);
        var variable = list.FirstOrDefault(item => item.Name == command.Name);
        if (variable is null)
            return NotFound("variable.notFound", "Variable", command.Name, VariableResource(command.Scope, command.Name));
        list.Remove(variable);
        SyncSettingsVariables(document);
        return Success($"Deleted {command.Scope.ToString().ToLowerInvariant()} variable '{command.Name}'.", "History.Variable.Delete", VariableResource(command.Scope, command.Name), command.Name);
    }

    private static CommandExecution MoveVariable(EditorProjectDocument document, MoveVariableDefinitionCommand command)
    {
        var list = Variables(document, command.Scope);
        var variable = list.FirstOrDefault(item => item.Name == command.Name);
        if (variable is null)
            return NotFound("variable.notFound", "Variable", command.Name, VariableResource(command.Scope, command.Name));
        if (command.Index < 0 || command.Index >= list.Count)
            return InvalidIndex("variable", command.Index, list.Count);
        list.Remove(variable);
        list.Insert(command.Index, variable);
        SyncSettingsVariables(document);
        return Success($"Moved variable '{command.Name}' to index {command.Index}.", "History.Variable.Move", VariableResource(command.Scope, command.Name), command.Name, command.Index);
    }

    private static CommandExecution RenameVariable(EditorProjectDocument document, RenameVariableDefinitionCommand command)
    {
        var list = Variables(document, command.Scope);
        var variable = list.FirstOrDefault(item => item.Name == command.Name);
        if (variable is null)
            return NotFound("variable.notFound", "Variable", command.Name, VariableResource(command.Scope, command.Name));
        if (!IsValidVariableName(command.NewName))
            return Error("variable.invalidName", $"Variable name '{command.NewName}' is invalid. Use ASCII letters, digits, and underscores.");
        if (AllVariables(document).Any(item => !ReferenceEquals(item, variable) && item.Name == command.NewName))
            return Error("variable.duplicateName", $"Variable '{command.NewName}' already exists.");
        if (command.UpdateReferences)
            ReplaceVariableReferences(document, command.Name, command.NewName);
        variable.Name = command.NewName;
        variable.DefaultValue.Name = command.NewName;
        SyncSettingsVariables(document);
        return Success($"Renamed variable '{command.Name}' to '{command.NewName}'.", "History.Variable.Rename", VariableResource(command.Scope, command.NewName), command.Name, command.NewName);
    }

    private static CommandExecution SetVariableType(EditorProjectDocument document, SetVariableDefinitionTypeCommand command)
    {
        var variable = Variables(document, command.Scope).FirstOrDefault(item => item.Name == command.Name);
        if (variable is null)
            return NotFound("variable.notFound", "Variable", command.Name, VariableResource(command.Scope, command.Name));
        variable.Type = command.Type;
        SyncSettingsVariables(document);
        return Success($"Set variable '{command.Name}' type to {command.Type}.", "History.Variable.SetType", VariableResource(command.Scope, command.Name), command.Name, command.Type);
    }

    private static CommandExecution SetVariableDefault(EditorProjectDocument document, SetVariableDefaultValueCommand command)
    {
        var variable = Variables(document, command.Scope).FirstOrDefault(item => item.Name == command.Name);
        if (variable is null)
            return NotFound("variable.notFound", "Variable", command.Name, VariableResource(command.Scope, command.Name));
        if (!TrySetVariableValue(variable, command.Value, out var message))
            return Error("variable.invalidDefault", message!);
        SyncSettingsVariables(document);
        return Success($"Updated default value for variable '{command.Name}'.", "History.Variable.SetDefault", VariableResource(command.Scope, command.Name), command.Name);
    }

    private static CommandExecution RenameProject(EditorProjectDocument document, RenameProjectCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            return Error("project.nameRequired", "Project name is required.");
        var oldName = document.Graph.Name;
        document.Graph.Name = command.Name.Trim();
        return Success($"Renamed project '{oldName}' to '{document.Graph.Name}'.", "History.Project.Rename", "project", oldName, document.Graph.Name);
    }

    private static CommandExecution PatchSettings(EditorProjectDocument document, PatchProjectSettingsCommand command)
    {
        if (command.Values.Count == 0)
            return Error("project.settings.emptyPatch", "At least one project setting is required.");
        foreach (var (key, value) in command.Values)
        {
            try
            {
                switch (key)
                {
                    case "defaultWidth":
                        document.Settings.DefaultWidth = PositiveInt(value, key);
                        break;
                    case "defaultHeight":
                        document.Settings.DefaultHeight = PositiveInt(value, key);
                        break;
                    case "saveSlotCount":
                        document.Settings.SaveSlotCount = NonNegativeInt(value, key);
                        break;
                    case "sfxChannelCount":
                        document.Settings.SfxChannelCount = PositiveInt(value, key);
                        break;
                    case "targetLocale":
                        var locale = new I18nLocale(value.GetString() ?? throw new JsonException("A locale string is required."));
                        document.Settings.TargetLocale = locale;
                        if (!document.Settings.AvailableLocales.Contains(locale))
                            document.Settings.AvailableLocales.Add(locale);
                        break;
                    default:
                        return Error("project.settings.unknownField", $"Project setting '{key}' is not editable through this command.");
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
            {
                return Error("project.settings.invalidValue", $"Invalid value for '{key}': {exception.Message}");
            }
        }
        return Success($"Updated {command.Values.Count} project setting(s).", "History.ProjectSettings.Patch", ["project/settings"], command.Values.Count);
    }

    private static int PositiveInt(JsonElement value, string key)
    {
        var result = value.GetInt32();
        return result > 0 ? result : throw new InvalidOperationException($"'{key}' must be greater than zero.");
    }

    private static CommandExecution ApplyUiPreset(EditorProjectDocument document, ApplyUiPresetCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.PresetId))
            return Error("ui.preset.idRequired", "A UI preset ID is required.");
        if (command.Defaults?.Keys.Any(key => !AllowedUiSettingKeys.Contains(key)) == true)
            return Error("ui.settings.unknownKey", "One or more preset defaults use an unknown UI setting key.");
        var selection = document.UiProject.GetPage(command.Page);
        selection.SwitchPreset(command.PresetId.Trim(), command.Defaults ?? new Dictionary<string, string>());
        return Success($"Set the {command.Page} UI page preset to '{selection.PresetId}'.", "History.Ui.SetPreset", $"ui/pages/{command.Page}", command.Page, selection.PresetId);
    }

    private static CommandExecution SetUiValue(EditorProjectDocument document, SetUiProjectValueCommand command) =>
        PatchUiValues(document, new PatchUiProjectValuesCommand(command.Page, new Dictionary<string, string?> { [command.Key] = command.Value }));

    private static CommandExecution PatchUiValues(EditorProjectDocument document, PatchUiProjectValuesCommand command)
    {
        if (command.Values.Count == 0)
            return Error("ui.settings.emptyPatch", "At least one UI setting is required.");
        var selection = document.UiProject.GetPage(command.Page);
        foreach (var (key, value) in command.Values)
        {
            if (string.IsNullOrWhiteSpace(key))
                return Error("ui.settings.keyRequired", "UI setting keys cannot be empty.");
            if (!AllowedUiSettingKeys.Contains(key))
                return Error("ui.settings.unknownKey", $"UI setting '{key}' is not approved by the editor schema.");
            if (value is null) selection.Settings.Remove(key);
            else selection.Settings[key] = value;
        }
        selection.SaveActivePresetSettings();
        return Success($"Updated {command.Values.Count} setting(s) on the {command.Page} UI page.", "History.Ui.PatchSettings", $"ui/pages/{command.Page}", command.Page, command.Values.Count);
    }

    private static CommandExecution ResetUiValues(EditorProjectDocument document, ResetUiProjectValuesCommand command)
    {
        if (command.Keys.Count == 0)
            return Error("ui.settings.emptyReset", "At least one UI setting key is required.");
        var selection = document.UiProject.GetPage(command.Page);
        foreach (var key in command.Keys)
        {
            if (string.IsNullOrWhiteSpace(key))
                return Error("ui.settings.keyRequired", "UI setting keys cannot be empty.");
            if (!AllowedUiSettingKeys.Contains(key))
                return Error("ui.settings.unknownKey", $"UI setting '{key}' is not approved by the editor schema.");
            if (command.Defaults?.TryGetValue(key, out var value) == true) selection.Settings[key] = value;
            else selection.Settings.Remove(key);
        }
        selection.SaveActivePresetSettings();
        return Success($"Reset {command.Keys.Count} setting(s) on the {command.Page} UI page.", "History.Ui.ResetSettings", $"ui/pages/{command.Page}", command.Page, command.Keys.Count);
    }

    private static CommandExecution ApplyUiPalette(EditorProjectDocument document, ApplyUiColorPaletteCommand command)
    {
        if (!GalNet.Core.UI.UiColorPalettePresets.All.Any(item => string.Equals(item.Id, command.PaletteId, StringComparison.Ordinal)))
            return Error("ui.palette.notFound", $"UI color palette '{command.PaletteId}' was not found.");
        GalNet.Core.UI.UiColorPalettePresets.Apply(document.UiProject, command.PaletteId);
        return Success($"Applied UI color palette '{command.PaletteId}'.", "History.Ui.ApplyPalette", "ui/palette", command.PaletteId);
    }

    private static CommandExecution ReplaceUiProject(EditorProjectDocument document, ReplaceUiProjectCommand command)
    {
        if (command.Project is null)
            return Error("ui.project.required", "A UI project document is required.");
        document.UiProject = command.Project;
        return Success("Replaced the UI project document.", "History.Ui.ReplaceProject", "ui");
    }

    private static int NonNegativeInt(JsonElement value, string key)
    {
        var result = value.GetInt32();
        return result >= 0 ? result : throw new InvalidOperationException($"'{key}' cannot be negative.");
    }

    private static bool TrySetVariableValue(ProjectVariableDefinition definition, JsonElement value, out string? message)
    {
        try
        {
            switch (definition.Type)
            {
                case VariableType.Bool:
                    definition.DefaultValue.SetValue(value.GetBoolean());
                    break;
                case VariableType.Int:
                    definition.DefaultValue.SetValue(value.GetInt32());
                    break;
                case VariableType.Float:
                    definition.DefaultValue.SetValue(value.GetSingle());
                    break;
                default:
                    definition.DefaultValue.SetValue(value.GetString() ?? "");
                    break;
            }
            message = null;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            message = $"Value is not compatible with {definition.Type}: {exception.Message}";
            return false;
        }
    }

    private static void ReplaceVariableReferences(EditorProjectDocument document, string oldName, string newName)
    {
        var pattern = $@"\b{Regex.Escape(oldName)}\b";
        foreach (var entry in document.GroupEntries.Values.SelectMany(value => value))
        {
            entry.Condition = Regex.Replace(entry.Condition, pattern, newName);
            entry.Parameters = Regex.Replace(entry.Parameters, pattern, newName);
        }
        foreach (var node in document.Graph.Nodes)
        {
            foreach (var option in node.Options ?? [])
                option.Condition = Regex.Replace(option.Condition, pattern, newName);
            foreach (var condition in node.Conditions ?? [])
                condition.Expression = Regex.Replace(condition.Expression, pattern, newName);
        }
    }

    private static void SyncSettingsVariables(EditorProjectDocument document)
    {
        document.Settings.PlayerVariables = document.Graph.PlayerVariables.Select(item => item.Clone()).ToList();
        document.Settings.SaveVariables = document.Graph.SaveVariables.Select(item => item.Clone()).ToList();
    }

    private static List<ProjectVariableDefinition> Variables(EditorProjectDocument document, VariableScope scope) =>
        scope == VariableScope.Player ? document.Graph.PlayerVariables : document.Graph.SaveVariables;

    private static IEnumerable<ProjectVariableDefinition> AllVariables(EditorProjectDocument document) =>
        document.Graph.PlayerVariables.Concat(document.Graph.SaveVariables);

    private static bool IsValidVariableName(string value) =>
        !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, "^[A-Za-z_][A-Za-z0-9_]*$");

    private static EditorGraphNodeDto? FindNode(EditorProjectDocument document, string nodeId) =>
        document.Graph.Nodes.FirstOrDefault(node => node.Id == nodeId);

    private static bool IsEntryNode(EditorGraphNodeDto node) =>
        node.Type.Equals("Entry", StringComparison.OrdinalIgnoreCase);

    private static bool IsGroupNode(EditorGraphNodeDto node) =>
        node.Type.Equals("Group", StringComparison.OrdinalIgnoreCase);

    private static bool IsChoiceNode(EditorGraphNodeDto node) =>
        node.Type.Equals("Branch", StringComparison.OrdinalIgnoreCase) &&
        node.BranchType?.Equals("Choice", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsConditionNode(EditorGraphNodeDto node) =>
        node.Type.Equals("Branch", StringComparison.OrdinalIgnoreCase) &&
        node.BranchType?.Equals("Condition", StringComparison.OrdinalIgnoreCase) == true;

    private static int OutputCount(EditorGraphNodeDto node)
    {
        if (IsChoiceNode(node)) return Math.Max(1, node.Options?.Count ?? 0);
        if (IsConditionNode(node)) return Math.Max(1, node.Conditions?.Count ?? 0);
        return 1;
    }

    private static bool TryGetGroup(
        EditorProjectDocument document,
        string groupId,
        out List<EditorEntryData>? entries,
        out CommandExecution? failure)
    {
        var node = FindNode(document, groupId);
        if (node is null || !IsGroupNode(node))
        {
            entries = null;
            failure = Error("group.notFound", $"Linear group '{groupId}' does not exist.", NodeResource(groupId));
            return false;
        }
        if (!document.GroupEntries.TryGetValue(groupId, out entries))
        {
            entries = [];
            document.GroupEntries[groupId] = entries;
        }
        failure = null;
        return true;
    }

    private static bool TryFindEntry(
        EditorProjectDocument document,
        string groupId,
        string entryId,
        out List<EditorEntryData>? entries,
        out EditorEntryData? entry,
        out CommandExecution? failure)
    {
        if (!TryGetGroup(document, groupId, out entries, out failure))
        {
            entry = null;
            return false;
        }
        entry = entries!.FirstOrDefault(item => item.StableId == entryId);
        if (entry is not null) return true;
        failure = Error("group.entry.notFound", $"Entry '{entryId}' does not exist in group '{groupId}'.", EntryResource(groupId, entryId));
        return false;
    }

    private static bool TryGetChoiceNode(EditorProjectDocument document, string nodeId, out EditorGraphNodeDto? node, out CommandExecution? failure)
    {
        node = FindNode(document, nodeId);
        if (node is null || !IsChoiceNode(node))
        {
            failure = Error("branch.choice.notFound", $"Choice branch '{nodeId}' does not exist.", NodeResource(nodeId));
            return false;
        }
        node.Options ??= [];
        failure = null;
        return true;
    }

    private static bool TryFindOption(
        EditorProjectDocument document,
        string nodeId,
        string optionId,
        out EditorGraphNodeDto? node,
        out EditorGraphBranchOptionDto? option,
        out CommandExecution? failure)
    {
        if (!TryGetChoiceNode(document, nodeId, out node, out failure))
        {
            option = null;
            return false;
        }
        option = node!.Options!.FirstOrDefault(item => item.Id == optionId);
        if (option is not null) return true;
        failure = Error("branch.option.notFound", $"Choice option '{optionId}' does not exist on node '{nodeId}'.", OptionResource(nodeId, optionId));
        return false;
    }

    private static bool TryGetConditionNode(EditorProjectDocument document, string nodeId, out EditorGraphNodeDto? node, out CommandExecution? failure)
    {
        node = FindNode(document, nodeId);
        if (node is null || !IsConditionNode(node))
        {
            failure = Error("branch.conditionNode.notFound", $"Condition branch '{nodeId}' does not exist.", NodeResource(nodeId));
            return false;
        }
        node.Conditions ??= [];
        failure = null;
        return true;
    }

    private static bool TryFindCondition(
        EditorProjectDocument document,
        string nodeId,
        string conditionId,
        out EditorGraphNodeDto? node,
        out EditorGraphBranchConditionDto? condition,
        out CommandExecution? failure)
    {
        if (!TryGetConditionNode(document, nodeId, out node, out failure))
        {
            condition = null;
            return false;
        }
        condition = node!.Conditions!.FirstOrDefault(item => item.Id == conditionId);
        if (condition is not null) return true;
        failure = Error("branch.condition.notFound", $"Branch condition '{conditionId}' does not exist on node '{nodeId}'.", ConditionResource(nodeId, conditionId));
        return false;
    }

    private static void ShiftOutletsForInsert(EditorProjectDocument document, string nodeId, int index)
    {
        foreach (var edge in document.Graph.Edges.Where(edge => edge.FromNodeId == nodeId && edge.FromOutlet >= index))
            edge.FromOutlet++;
    }

    private static void RemoveOutletAndShift(EditorProjectDocument document, string nodeId, int index)
    {
        document.Graph.Edges.RemoveAll(edge => edge.FromNodeId == nodeId && edge.FromOutlet == index);
        foreach (var edge in document.Graph.Edges.Where(edge => edge.FromNodeId == nodeId && edge.FromOutlet > index))
            edge.FromOutlet--;
    }

    private static void RemapMovedOutlet(EditorProjectDocument document, string nodeId, int oldIndex, int newIndex)
    {
        foreach (var edge in document.Graph.Edges.Where(edge => edge.FromNodeId == nodeId))
        {
            if (edge.FromOutlet == oldIndex) edge.FromOutlet = newIndex;
            else if (oldIndex < newIndex && edge.FromOutlet > oldIndex && edge.FromOutlet <= newIndex) edge.FromOutlet--;
            else if (newIndex < oldIndex && edge.FromOutlet >= newIndex && edge.FromOutlet < oldIndex) edge.FromOutlet++;
        }
    }

    private static Dictionary<string, string> ParseParameters(string value) =>
        value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : "", StringComparer.Ordinal);

    private static string SerializeParameters(IEnumerable<KeyValuePair<string, string>> parameters) =>
        string.Join("; ", parameters.Select(pair => $"{pair.Key}={pair.Value}"));

    private static void Renumber(IReadOnlyList<EditorEntryData> entries)
    {
        for (var index = 0; index < entries.Count; index++)
            entries[index].Id = index + 1;
    }

    private static CommandExecution InvalidIndex(string kind, int index, int count) =>
        Error("command.invalidIndex", $"Index {index} is outside the valid range for {kind} collection of size {count}.");

    private static CommandExecution NotFound(string code, string kind, string id, string resource) =>
        Error(code, $"{kind} '{id}' does not exist.", resource);

    private static CommandExecution Error(string code, string message, string? resource = null) =>
        CommandExecution.Failed(EditorDiagnostic.Error(code, message, resource));

    private static CommandExecution Success(
        string description,
        string displayNameKey,
        string changedResource,
        params object?[] arguments) =>
        CommandExecution.Succeeded(description, displayNameKey, [changedResource], arguments);

    private static CommandExecution Success(
        string description,
        string displayNameKey,
        IReadOnlyList<string> changedResources,
        params object?[] arguments) =>
        CommandExecution.Succeeded(description, displayNameKey, changedResources, arguments);

    private static string NodeResource(string nodeId) => $"graph/nodes/{nodeId}";
    private static string EdgeResource(string edgeId) => $"graph/edges/{edgeId}";
    private static string EntryResource(string groupId, string entryId) => $"groups/{groupId}/entries/{entryId}";
    private static string OptionResource(string nodeId, string optionId) => $"graph/nodes/{nodeId}/options/{optionId}";
    private static string ConditionResource(string nodeId, string conditionId) => $"graph/nodes/{nodeId}/conditions/{conditionId}";
    private static string VariableResource(VariableScope scope, string name) => $"variables/{scope.ToString().ToLowerInvariant()}/{name}";
}
