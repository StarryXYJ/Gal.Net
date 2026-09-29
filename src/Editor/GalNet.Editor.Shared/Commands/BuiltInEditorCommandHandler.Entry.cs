using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Commands;

public sealed partial class BuiltInEditorCommandHandler
{
    private static CommandExecution AddEntry(EditorProjectDocument document, AddEntryCommand command, IEntryCatalog catalog)
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
        var type = command.Type.Trim();
        if (!catalog.TryGet(type, out var definition))
            return Error("group.entry.unknownType", $"Unknown entry type '{type}'.");
        var entry = new EditorEntryData
        {
            StableId = command.EntryId,
            Type = type,
            Condition = command.Condition ?? "",
            Parameters = catalog.Create(type, values: command.Parameters).Values
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

    private static CommandExecution SetEntryType(EditorProjectDocument document, SetEntryTypeCommand command, IEntryCatalog catalog)
    {
        if (!TryFindEntry(document, command.GroupId, command.EntryId, out _, out var entry, out var failure))
            return failure!;
        if (string.IsNullOrWhiteSpace(command.Type))
            return Error("group.entry.typeRequired", "Entry type is required.");
        var type = command.Type.Trim();
        if (!catalog.TryGet(type, out _))
            return Error("group.entry.unknownType", $"Unknown entry type '{type}'.");
        entry!.Type = type;
        entry.Parameters = new Dictionary<string, string>(catalog.Create(type).Values, StringComparer.Ordinal);
        return Success($"Set entry '{command.EntryId}' type to '{entry.Type}'.", "History.Entry.SetType", EntryResource(command.GroupId, command.EntryId), command.EntryId, entry.Type);
    }

    private static CommandExecution SetEntryCondition(EditorProjectDocument document, SetEntryConditionCommand command)
    {
        if (!TryFindEntry(document, command.GroupId, command.EntryId, out _, out var entry, out var failure))
            return failure!;
        entry!.Condition = command.Condition ?? "";
        return Success($"Updated condition for entry '{command.EntryId}'.", "History.Entry.SetCondition", EntryResource(command.GroupId, command.EntryId), command.EntryId);
    }

    private static CommandExecution SetEntryParameters(EditorProjectDocument document, SetEntryParametersCommand command, IEntryCatalog catalog)
    {
        if (!TryFindEntry(document, command.GroupId, command.EntryId, out _, out var entry, out var failure))
            return failure!;
        entry!.Parameters = new Dictionary<string, string>(catalog.Create(entry.Type, values: command.Parameters).Values, StringComparer.Ordinal);
        return Success($"Replaced parameters for entry '{command.EntryId}'.", "History.Entry.SetParameters", EntryResource(command.GroupId, command.EntryId), command.EntryId);
    }

    private static CommandExecution PatchEntryParameters(EditorProjectDocument document, PatchEntryParametersCommand command, IEntryCatalog catalog)
    {
        if (!TryFindEntry(document, command.GroupId, command.EntryId, out _, out var entry, out var failure))
            return failure!;
        var parameters = new Dictionary<string, string>(entry!.Parameters, StringComparer.Ordinal);
        foreach (var (key, value) in command.Parameters)
        {
            if (string.IsNullOrWhiteSpace(key))
                return Error("group.entry.parameterKeyRequired", "Entry parameter keys cannot be empty.");
            if (value is null) parameters.Remove(key);
            else parameters[key] = value;
        }
        entry.Parameters = new Dictionary<string, string>(catalog.Create(entry.Type, values: parameters).Values, StringComparer.Ordinal);
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

    private static void Renumber(IReadOnlyList<EditorEntryData> entries)
    {
        for (var index = 0; index < entries.Count; index++)
            entries[index].Id = index + 1;
    }

}
