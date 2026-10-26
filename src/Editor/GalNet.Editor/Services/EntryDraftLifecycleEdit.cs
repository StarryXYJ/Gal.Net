using GalNet.Editor.History;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

public sealed record EntryAddressTransition(SceneAuthorAddress Before, SceneAuthorAddress After);

/// <summary>Content and input lifecycle share the history gate. Observers run only after both are stable.</summary>
internal sealed class EntryDraftLifecycleEdit(EntryEditSession inputs, IUndoableEdit edit, IReadOnlyList<EntryAddressTransition> addresses, AuthorChangeJournal journal) : IUndoableEdit, IAuthorScopedEdit
{
    public bool HasKnownAuthorScope => UndoRedoHistory.KnownScope(edit);
    public string Description => edit.Description;
    public EditorText DisplayText => edit.DisplayText;
    public long RetainedBytes => edit.RetainedBytes;
    public void Redo() => inputs.ApplyAuthorEdit(addresses, edit.Redo, journal);
    public void Undo() => inputs.ApplyAuthorEdit(addresses.Select(address => new EntryAddressTransition(address.After, address.Before)).ToArray(), edit.Undo, journal);
}
