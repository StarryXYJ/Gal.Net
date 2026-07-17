using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Sessions;

namespace GalNet.Editor.Shared.Commands;

public sealed class EditorSession : IEditorSession
{
    private readonly IReadOnlyList<IEditorCommandHandler> _handlers;
    private readonly EditorDocumentValidator _validator;
    private readonly IEditorSessionPersistence _persistence;
    private readonly UndoRedoHistory _history = new();

    public EditorProjectDocument Document { get; private set; }
    public long Revision { get; private set; }
    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
    public bool IsDirty => _history.IsDirty;
    public string? UndoDescription => _history.UndoDescription;
    public string? RedoDescription => _history.RedoDescription;

    public event Action? DocumentChanged;
    public event Action? HistoryChanged;

    public EditorSession(
        EditorProjectDocument document,
        IEditorSessionPersistence persistence,
        IEnumerable<IEditorCommandHandler>? handlers = null,
        EditorDocumentValidator? validator = null)
    {
        Document = EditorDocumentCloner.Clone(document);
        _persistence = persistence;
        _handlers = (handlers ?? [new BuiltInEditorCommandHandler()]).ToList();
        _validator = validator ?? new EditorDocumentValidator();
        _history.MarkSaved();
    }

    public CommandResult Execute(IProjectEditCommand command, ExecuteOptions? options = null) =>
        ExecuteTransaction([command], options);

    public CommandResult ExecuteTransaction(
        IReadOnlyList<IProjectEditCommand> commands,
        ExecuteOptions? options = null)
    {
        options ??= new ExecuteOptions();
        if (options.ExpectedRevision is { } expected && expected != Revision)
        {
            return CommandResult.Failure(
                Revision,
                EditorDiagnostic.Error(
                    "session.revisionConflict",
                    $"Expected revision {expected}, but the current revision is {Revision}."));
        }
        if (commands.Count == 0)
            return CommandResult.Failure(Revision, EditorDiagnostic.Error("command.emptyTransaction", "A transaction must contain at least one command."));

        var before = EditorDocumentCloner.Clone(Document);
        var working = EditorDocumentCloner.Clone(Document);
        var descriptions = new List<EditorExecutionDescription>();
        var resources = new HashSet<string>(StringComparer.Ordinal);
        var diagnostics = new List<EditorDiagnostic>();

        foreach (var command in commands)
        {
            var handler = _handlers.FirstOrDefault(candidate => candidate.CanHandle(command));
            if (handler is null)
            {
                diagnostics.Add(EditorDiagnostic.Error("command.noHandler", $"No handler is registered for '{command.CommandId}'."));
                return Failed(diagnostics);
            }

            var execution = handler.Execute(working, command, new EditorCommandContext(Revision, options.DryRun));
            diagnostics.AddRange(execution.Diagnostics);
            if (!execution.Success)
                return Failed(diagnostics);
            if (execution.Description is not null)
                descriptions.Add(execution.Description);
            resources.UnionWith(execution.ChangedResources);
        }

        var validation = _validator.Validate(working);
        diagnostics.AddRange(validation.Diagnostics);
        if (!validation.IsValid)
            return Failed(diagnostics);

        var description = CombineDescriptions(descriptions);
        var transactionId = $"tx_{Guid.NewGuid():N}";
        if (!options.DryRun)
        {
            var change = new EditorDocumentChange(before, working, resources.ToList());
            Document = change.Apply();
            _history.Push(change, description, options.MergeKey, options.MergeWindow);
            Revision++;
            DocumentChanged?.Invoke();
            HistoryChanged?.Invoke();
        }

        return new CommandResult(
            true,
            Revision,
            transactionId,
            description.Description,
            description.DisplayNameKey,
            description.DisplayNameArguments,
            resources.ToList(),
            diagnostics);

        CommandResult Failed(IReadOnlyList<EditorDiagnostic> failureDiagnostics) =>
            new(false, Revision, null, null, null, [], [], failureDiagnostics);
    }

    public CommandResult Undo()
    {
        if (!CanUndo)
            return CommandResult.Failure(Revision, EditorDiagnostic.Error("history.cannotUndo", "There is no command to undo."));
        var entry = _history.Undo();
        Document = entry.Change.Revert();
        Revision++;
        DocumentChanged?.Invoke();
        HistoryChanged?.Invoke();
        return HistoryResult("Undid", entry);
    }

    public CommandResult Redo()
    {
        if (!CanRedo)
            return CommandResult.Failure(Revision, EditorDiagnostic.Error("history.cannotRedo", "There is no command to redo."));
        var entry = _history.Redo();
        Document = entry.Change.Apply();
        Revision++;
        DocumentChanged?.Invoke();
        HistoryChanged?.Invoke();
        return HistoryResult("Redid", entry);
    }

    public ValidationResult Validate() => _validator.Validate(Document);

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var validation = Validate();
        if (!validation.IsValid)
            throw new InvalidOperationException("The editor document contains validation errors and cannot be saved.");
        await _persistence.SaveAsync(EditorDocumentCloner.Clone(Document), cancellationToken);
        _history.MarkSaved();
        HistoryChanged?.Invoke();
    }

    private CommandResult HistoryResult(string action, UndoRedoHistory.HistoryEntry entry) =>
        new(
            true,
            Revision,
            null,
            $"{action} {entry.Description.Description}",
            entry.Description.DisplayNameKey,
            entry.Description.DisplayNameArguments,
            entry.Change.ChangedResources,
            []);

    private static EditorExecutionDescription CombineDescriptions(IReadOnlyList<EditorExecutionDescription> descriptions)
    {
        if (descriptions.Count == 1)
            return descriptions[0];
        return new EditorExecutionDescription(
            $"Executed {descriptions.Count} editor commands: {string.Join(" ", descriptions.Select(item => item.Description))}",
            new GalNet.Core.I18n.I18nKey("History.Transaction"),
            [descriptions.Count]);
    }
}
