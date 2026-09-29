using System.Text.Json;
using System.Text.RegularExpressions;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Commands;

public sealed partial class BuiltInEditorCommandHandler
{
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
            entry.Parameters = entry.Parameters.ToDictionary(pair => pair.Key, pair => Regex.Replace(pair.Value, pattern, newName), StringComparer.Ordinal);
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

}
