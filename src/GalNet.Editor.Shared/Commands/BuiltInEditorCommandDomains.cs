using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Core.Entry;

namespace GalNet.Editor.Shared.Commands;

public sealed partial class BuiltInEditorCommandHandler
{
    private interface ICommandDomain
    {
        bool CanHandle(IProjectEditCommand command);
        CommandExecution Execute(EditorProjectDocument document, IProjectEditCommand command, EditorCommandContext context);
    }

    private readonly ICommandDomain[] Domains;

    public BuiltInEditorCommandHandler(IEntryCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Domains = [new GraphDomain(), new EntryDomain(catalog), new VariableDomain(), new GalleryDomain(), new ProjectDomain()];
    }

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
        private readonly IEntryCatalog _catalog;

        public EntryDomain(IEntryCatalog catalog) => _catalog = catalog;

        public bool CanHandle(IProjectEditCommand command) => command is AddEntryCommand or DeleteEntryCommand or MoveEntryCommand or SetEntryTypeCommand or SetEntryConditionCommand or SetEntryParametersCommand or PatchEntryParametersCommand or AddChoiceOptionCommand or DeleteChoiceOptionCommand or MoveChoiceOptionCommand or SetChoiceOptionTextCommand or SetChoiceOptionConditionCommand or AddBranchConditionCommand or DeleteBranchConditionCommand or MoveBranchConditionCommand or SetBranchConditionExpressionCommand;
        public CommandExecution Execute(EditorProjectDocument d, IProjectEditCommand c, EditorCommandContext _) => c switch
        {
            AddEntryCommand v => AddEntry(d, v, _catalog), DeleteEntryCommand v => DeleteEntry(d, v), MoveEntryCommand v => MoveEntry(d, v), SetEntryTypeCommand v => SetEntryType(d, v, _catalog), SetEntryConditionCommand v => SetEntryCondition(d, v), SetEntryParametersCommand v => SetEntryParameters(d, v, _catalog), PatchEntryParametersCommand v => PatchEntryParameters(d, v, _catalog), AddChoiceOptionCommand v => AddOption(d, v), DeleteChoiceOptionCommand v => DeleteOption(d, v), MoveChoiceOptionCommand v => MoveOption(d, v), SetChoiceOptionTextCommand v => SetOptionText(d, v), SetChoiceOptionConditionCommand v => SetOptionCondition(d, v), AddBranchConditionCommand v => AddCondition(d, v), DeleteBranchConditionCommand v => DeleteCondition(d, v), MoveBranchConditionCommand v => MoveCondition(d, v), SetBranchConditionExpressionCommand v => SetConditionExpression(d, v), _ => throw new InvalidOperationException()
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

    private sealed class GalleryDomain : ICommandDomain
    {
        public bool CanHandle(IProjectEditCommand command) => command is
            RegisterGalleryTypeCommand or RemoveGalleryTypeCommand or
            SetGalleryItemCommand or RemoveGalleryItemCommand;

        public CommandExecution Execute(EditorProjectDocument d, IProjectEditCommand c, EditorCommandContext _) => c switch
        {
            RegisterGalleryTypeCommand v => RegisterGalleryType(d, v),
            RemoveGalleryTypeCommand v => RemoveGalleryType(d, v),
            SetGalleryItemCommand v => SetGalleryItem(d, v),
            RemoveGalleryItemCommand v => RemoveGalleryItem(d, v),
            _ => throw new InvalidOperationException()
        };
    }
}
