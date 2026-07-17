using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.I18n;
using GalNet.Editor.Abstraction.Commands;

namespace GalNet.Editor.Shared.Commands;

public sealed class EditorCommandCatalog : IEditorCommandCatalog
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IReadOnlyList<IProjectCommandDefinition> _definitions;
    private readonly IReadOnlyDictionary<string, IProjectCommandDefinition> _byId;

    public EditorCommandCatalog()
    {
        _definitions = CreateDefinitions();
        _byId = _definitions.ToDictionary(definition => definition.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IProjectCommandDefinition> GetAll() => _definitions;

    public IProjectCommandDefinition? Find(string commandId) =>
        _byId.GetValueOrDefault(commandId);

    public IProjectEditCommand Deserialize(
        string commandId,
        JsonElement payload,
        JsonSerializerOptions? options = null)
    {
        if (!(_byId.TryGetValue(commandId, out var definition)))
            throw new KeyNotFoundException($"Unknown editor command '{commandId}'.");

        return (IProjectEditCommand?)payload.Deserialize(definition.CommandType, options ?? DefaultJsonOptions)
            ?? throw new JsonException($"Command '{commandId}' deserialized to null.");
    }

    private static IReadOnlyList<IProjectCommandDefinition> CreateDefinitions() =>
    [
        Define<CreateNodeCommand>("graph.node.create", "Creates a graph node with a stable identifier, type, name, and position."),
        Define<DeleteNodeCommand>("graph.node.delete", "Deletes a graph node and all edges connected to it."),
        Define<RenameNodeCommand>("graph.node.rename", "Changes the display name of a graph node."),
        Define<MoveNodesCommand>("graph.node.move", "Moves one or more graph nodes to new canvas positions."),
        Define<SetRootNodeCommand>("graph.node.setRoot", "Sets the graph entry or root node after validating graph invariants."),
        Define<ConnectNodesCommand>("graph.edge.connect", "Connects an output outlet to an input and replaces conflicting edges according to graph rules."),
        Define<DeleteEdgeCommand>("graph.edge.delete", "Deletes a graph edge identified by its stable ID or endpoints."),
        Define<AddEntryCommand>("group.entry.add", "Adds an entry to a linear group at the requested position."),
        Define<DeleteEntryCommand>("group.entry.delete", "Deletes an entry from a linear group."),
        Define<MoveEntryCommand>("group.entry.move", "Moves an entry to another position within its group."),
        Define<SetEntryTypeCommand>("group.entry.setType", "Changes an entry type and validates its parameters."),
        Define<SetEntryConditionCommand>("group.entry.setCondition", "Changes the conditional expression of an entry."),
        Define<SetEntryParametersCommand>("group.entry.setParameters", "Replaces the parameter map of an entry after schema validation."),
        Define<PatchEntryParametersCommand>("group.entry.patchParameters", "Updates selected entry parameters without replacing unrelated values."),
        Define<AddChoiceOptionCommand>("branch.option.add", "Adds an option to a choice branch at the requested position."),
        Define<DeleteChoiceOptionCommand>("branch.option.delete", "Deletes a choice option and updates or removes its outlet edge."),
        Define<MoveChoiceOptionCommand>("branch.option.move", "Reorders a choice option while preserving its associated edge."),
        Define<SetChoiceOptionTextCommand>("branch.option.setText", "Changes the text or localization key of a choice option."),
        Define<SetChoiceOptionConditionCommand>("branch.option.setCondition", "Changes the conditional expression of a choice option."),
        Define<AddBranchConditionCommand>("branch.condition.add", "Adds a condition route to a condition branch."),
        Define<DeleteBranchConditionCommand>("branch.condition.delete", "Deletes a condition route and updates or removes its outlet edge."),
        Define<MoveBranchConditionCommand>("branch.condition.move", "Reorders a branch condition while preserving its associated edge."),
        Define<SetBranchConditionExpressionCommand>("branch.condition.setExpression", "Changes the expression used by a condition branch route."),
        Define<AddVariableDefinitionCommand>("variable.definition.add", "Adds a player or save variable definition with a unique stable name."),
        Define<DeleteVariableDefinitionCommand>("variable.definition.delete", "Deletes a variable definition after reporting project references."),
        Define<MoveVariableDefinitionCommand>("variable.definition.move", "Reorders a variable definition within its scope."),
        Define<RenameVariableDefinitionCommand>("variable.definition.rename", "Renames a variable definition and optionally updates all references."),
        Define<SetVariableDefinitionTypeCommand>("variable.definition.setType", "Changes a variable type and converts or resets its default value."),
        Define<SetVariableDefaultValueCommand>("variable.definition.setDefault", "Changes the default value of a project variable."),
        Define<RenameProjectCommand>("project.rename", "Changes the project display name without moving its directory."),
        Define<PatchProjectSettingsCommand>("project.settings.patch", "Updates selected project settings after validating each supported field."),
        Define<ApplyUiPresetCommand>("ui.preset.apply", "Applies a registered UI preset to the project UI configuration."),
        Define<ApplyUiColorPaletteCommand>("ui.palette.apply", "Applies a registered color palette to the project UI configuration."),
        Define<SetUiProjectValueCommand>("ui.value.set", "Sets one schema-approved UI project value."),
        Define<PatchUiProjectValuesCommand>("ui.values.patch", "Updates multiple schema-approved UI project values as one transaction."),
        Define<ResetUiProjectValuesCommand>("ui.values.reset", "Resets selected UI project values to preset or built-in defaults.")
    ];

    private static IProjectCommandDefinition Define<TCommand>(string id, string description)
        where TCommand : IProjectEditCommand =>
        new ProjectCommandDefinition(
            id,
            description,
            new I18nKey(ToDisplayNameKey(id)),
            typeof(TCommand),
            BuildSchema(typeof(TCommand)));

    private static EditorCommandSchema BuildSchema(Type commandType)
    {
        var constructor = commandType.GetConstructors().OrderByDescending(item => item.GetParameters().Length).First();
        var parameters = constructor.GetParameters()
            .Where(parameter => !string.Equals(parameter.Name, "commandId", StringComparison.OrdinalIgnoreCase))
            .Select(parameter => new EditorCommandParameter(
                parameter.Name ?? "value",
                GetFriendlyTypeName(parameter.ParameterType),
                !parameter.HasDefaultValue && Nullable.GetUnderlyingType(parameter.ParameterType) is null,
                $"Value for {parameter.Name}."))
            .ToList();
        return new EditorCommandSchema(parameters);
    }

    private static string GetFriendlyTypeName(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string)) return "string";
        if (type == typeof(bool)) return "boolean";
        if (type == typeof(int) || type == typeof(long)) return "integer";
        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal)) return "number";
        if (type.IsEnum) return string.Join(" | ", Enum.GetNames(type));
        if (type.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(type)) return "array";
        return type.Name;
    }

    private static string ToDisplayNameKey(string id) =>
        "Command." + string.Join('.', id.Split('.').Select(segment => char.ToUpperInvariant(segment[0]) + segment[1..]));

    private sealed record ProjectCommandDefinition(
        string Id,
        string Description,
        I18nKey DisplayNameKey,
        Type CommandType,
        EditorCommandSchema Schema) : IProjectCommandDefinition;
}
