using System.Text.Json;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

/// <summary>An edit tied to its source snapshot, with optional impact analysis.</summary>
public sealed class PreparedEntryEdit
{
    private readonly IEditorDocumentService _documents;
    private readonly EditorHistories _histories;
    private readonly IUndoableEdit _edit;
    private readonly string _source;
    private readonly EditorGraphDocument _document;
    private readonly Func<bool>? _precondition;
    private bool _committed;
    private readonly Lazy<SceneEditImpact> _impact;
    public SceneEditImpact Impact => _impact.Value;
    internal PreparedEntryEdit(IEditorDocumentService documents, EditorHistories histories, IUndoableEdit edit, Action<EditorProjectDocument> apply, int archivedInputs = 0, Func<bool>? precondition = null,
        IReadOnlyDictionary<SceneAuthorAddress, SceneAuthorAddress>? originalOwners = null)
    {
        _documents = documents; _histories = histories; _edit = edit;
        _document = documents.CurrentDocument;
        _precondition = precondition;
        _source = Capture(documents);
        var source = _source;
        _impact = new(() =>
        {
            var before = JsonSerializer.Deserialize<EditorProjectDocument>(source)!;
            var after = JsonSerializer.Deserialize<EditorProjectDocument>(source)!;
            apply(after);
            return SceneReferenceAnalysis.Compare(before.Graph, before.GroupEntries, after.GroupEntries, originalOwners) with { ArchivedInputs = archivedInputs };
        });
    }
    public void Commit()
    {
        if (_committed) throw Stale();
        _histories.Content.Execute(new PreviewedEdit(_documents, _document, _source, _edit, _precondition));
        _committed = true;
    }
    private static string Capture(IEditorDocumentService documents) => JsonSerializer.Serialize(new EditorProjectDocument
    { Graph = documents.CurrentDocument, GroupEntries = documents.Groups.ToDictionary(pair => pair.Key, pair => pair.Value.Entries) });
    private static EditorOperationException Stale() => new("entry.impactChanged", "Author content changed while reviewing this operation. Review its effects again.");
    private sealed class PreviewedEdit(IEditorDocumentService documents, EditorGraphDocument document, string source, IUndoableEdit edit, Func<bool>? precondition) : IUndoableEdit, IAuthorScopedEdit
    {
        private string? _expected = source;
        private EditorGraphDocument? _expectedDocument = document;
        private Func<bool>? _precondition = precondition;
        public bool HasKnownAuthorScope => edit is IAuthorScopedEdit { HasKnownAuthorScope: true };
        public string Description => edit.Description;
        public EditorText DisplayText => edit.DisplayText;
        public long RetainedBytes => edit.RetainedBytes;
        public void Redo()
        {
            if (_expected is { } expected && (!documents.IsLoaded || !ReferenceEquals(documents.CurrentDocument, _expectedDocument) || Capture(documents) != expected || _precondition?.Invoke() == false)) throw Stale();
            edit.Redo(); _expected = null; _expectedDocument = null; _precondition = null;
        }
        public void Undo() => edit.Undo();
    }
}
