using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Abstraction.Sessions;

public interface IEditorSession
{
    EditorProjectDocument Document { get; }
    long Revision { get; }
    bool CanUndo { get; }
    bool CanRedo { get; }
    bool IsDirty { get; }
    string? UndoDescription { get; }
    string? RedoDescription { get; }

    event Action? DocumentChanged;
    event Action? HistoryChanged;

    CommandResult Execute(IProjectEditCommand command, ExecuteOptions? options = null);
    CommandResult ExecuteTransaction(IReadOnlyList<IProjectEditCommand> commands, ExecuteOptions? options = null);
    CommandResult Undo();
    CommandResult Redo();
    ValidationResult Validate();
    Task SaveAsync(CancellationToken cancellationToken = default);
}

public interface IEditorSessionPersistence
{
    Task SaveAsync(EditorProjectDocument document, CancellationToken cancellationToken = default);
}
