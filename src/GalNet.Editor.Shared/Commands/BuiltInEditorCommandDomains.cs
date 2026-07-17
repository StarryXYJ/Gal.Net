using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Commands;

public sealed partial class BuiltInEditorCommandHandler
{
    private interface ICommandDomain
    {
        bool CanHandle(IProjectEditCommand command);
        CommandExecution Execute(EditorProjectDocument document, IProjectEditCommand command, EditorCommandContext context);
    }

    private static readonly ICommandDomain[] Domains = [new GraphDomain(), new EntryDomain(), new VariableDomain(), new ProjectDomain(), new UiDomain()];

    private sealed class GraphDomain : ICommandDomain
    {
        public bool CanHandle(IProjectEditCommand command) => command is CreateNodeCommand or DeleteNodeCommand or RenameNodeCommand or MoveNodesCommand or SetRootNodeCommand or ConnectNodesCommand or DeleteEdgeCommand;
        public CommandExecution Execute(EditorProjectDocument d, IProjectEditCommand c, EditorCommandContext _) => c switch
        {
            CreateNodeCommand v => CreateNode(d, v), DeleteNodeCommand v => DeleteNode(d, v), RenameNodeCommand v => RenameNode(d, v), MoveNodesCommand v => MoveNodes(d, v), SetRootNodeCommand v => SetRoot(d, v), ConnectNodesCommand v => Connect(d, v), DeleteEdgeCommand v => DeleteEdge(d, v), _ => throw new InvalidOperationException()
        };
    }
    private sealed class EntryDomain : ICommandDomain
    {
        public bool CanHandle(IProjectEditCommand command) => command is AddEntryCommand or DeleteEntryCommand or MoveEntryCommand or SetEntryTypeCommand or SetEntryConditionCommand or SetEntryParametersCommand or PatchEntryParametersCommand or AddChoiceOptionCommand or DeleteChoiceOptionCommand or MoveChoiceOptionCommand or SetChoiceOptionTextCommand or SetChoiceOptionConditionCommand or AddBranchConditionCommand or DeleteBranchConditionCommand or MoveBranchConditionCommand or SetBranchConditionExpressionCommand;
        public CommandExecution Execute(EditorProjectDocument d, IProjectEditCommand c, EditorCommandContext _) => c switch
        {
            AddEntryCommand v => AddEntry(d, v), DeleteEntryCommand v => DeleteEntry(d, v), MoveEntryCommand v => MoveEntry(d, v), SetEntryTypeCommand v => SetEntryType(d, v), SetEntryConditionCommand v => SetEntryCondition(d, v), SetEntryParametersCommand v => SetEntryParameters(d, v), PatchEntryParametersCommand v => PatchEntryParameters(d, v), AddChoiceOptionCommand v => AddOption(d, v), DeleteChoiceOptionCommand v => DeleteOption(d, v), MoveChoiceOptionCommand v => MoveOption(d, v), SetChoiceOptionTextCommand v => SetOptionText(d, v), SetChoiceOptionConditionCommand v => SetOptionCondition(d, v), AddBranchConditionCommand v => AddCondition(d, v), DeleteBranchConditionCommand v => DeleteCondition(d, v), MoveBranchConditionCommand v => MoveCondition(d, v), SetBranchConditionExpressionCommand v => SetConditionExpression(d, v), _ => throw new InvalidOperationException()
        };
    }
    private sealed class VariableDomain : ICommandDomain
    {
        public bool CanHandle(IProjectEditCommand command) => command is AddVariableDefinitionCommand or DeleteVariableDefinitionCommand or MoveVariableDefinitionCommand or RenameVariableDefinitionCommand or SetVariableDefinitionTypeCommand or SetVariableDefaultValueCommand;
        public CommandExecution Execute(EditorProjectDocument d, IProjectEditCommand c, EditorCommandContext _) => c switch
        {
            AddVariableDefinitionCommand v => AddVariable(d, v), DeleteVariableDefinitionCommand v => DeleteVariable(d, v), MoveVariableDefinitionCommand v => MoveVariable(d, v), RenameVariableDefinitionCommand v => RenameVariable(d, v), SetVariableDefinitionTypeCommand v => SetVariableType(d, v), SetVariableDefaultValueCommand v => SetVariableDefault(d, v), _ => throw new InvalidOperationException()
        };
    }
    private sealed class ProjectDomain : ICommandDomain
    {
        public bool CanHandle(IProjectEditCommand command) => command is RenameProjectCommand or PatchProjectSettingsCommand;
        public CommandExecution Execute(EditorProjectDocument d, IProjectEditCommand c, EditorCommandContext _) => c switch { RenameProjectCommand v => RenameProject(d, v), PatchProjectSettingsCommand v => PatchSettings(d, v), _ => throw new InvalidOperationException() };
    }
    private sealed class UiDomain : ICommandDomain
    {
        public bool CanHandle(IProjectEditCommand command) => command is ApplyUiPresetCommand or SetUiProjectValueCommand or PatchUiProjectValuesCommand or ResetUiProjectValuesCommand or ApplyUiColorPaletteCommand or ReplaceUiProjectCommand;
        public CommandExecution Execute(EditorProjectDocument d, IProjectEditCommand c, EditorCommandContext _) => c switch
        {
            ApplyUiPresetCommand v => ApplyUiPreset(d, v), SetUiProjectValueCommand v => SetUiValue(d, v), PatchUiProjectValuesCommand v => PatchUiValues(d, v), ResetUiProjectValuesCommand v => ResetUiValues(d, v), ApplyUiColorPaletteCommand v => ApplyUiPalette(d, v), ReplaceUiProjectCommand v => ReplaceUiProject(d, v), _ => throw new InvalidOperationException()
        };
    }
}
