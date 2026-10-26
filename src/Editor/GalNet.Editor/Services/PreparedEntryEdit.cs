using System.Text.Json;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

/// <summary>A read-only preview tied to its source snapshot. Confirmation cannot silently target newer content.</summary>
public sealed class PreparedEntryEdit
{
    private readonly IEditorDocumentService _documents;
    private readonly EditorHistories _histories;
    private readonly IUndoableEdit _edit;
    private readonly string _source;
    private readonly EditorGraphDocument _document;
    private bool _committed;
    public SceneEditImpact Impact { get; }
    internal PreparedEntryEdit(IEditorDocumentService documents, EditorHistories histories, IUndoableEdit edit, Action<EditorProjectDocument> apply)
    {
        _documents = documents; _histories = histories; _edit = edit;
        _document = documents.CurrentDocument;
        _source = Capture(documents);
        var before = JsonSerializer.Deserialize<EditorProjectDocument>(_source)!;
        var after = JsonSerializer.Deserialize<EditorProjectDocument>(_source)!;
        apply(after);
        Impact = SceneReferenceAnalysis.Compare(before.Graph, before.GroupEntries, after.GroupEntries);
    }
    public void Commit()
    {
        if (_committed) throw Stale();
        _histories.Content.Execute(new PreviewedEdit(_documents, _document, _source, _edit));
        _committed = true;
    }
    private static string Capture(IEditorDocumentService documents) => JsonSerializer.Serialize(new EditorProjectDocument
    { Graph = documents.CurrentDocument, GroupEntries = documents.Groups.ToDictionary(pair => pair.Key, pair => pair.Value.Entries) });
    private static EditorOperationException Stale() => new("entry.impactChanged", "Author content changed while reviewing this operation. Review its effects again.");
    private sealed class PreviewedEdit(IEditorDocumentService documents, EditorGraphDocument document, string source, IUndoableEdit edit) : IUndoableEdit, IAuthorScopedEdit
    {
        private string? _expected = source;
        private EditorGraphDocument? _expectedDocument = document;
        public bool HasKnownAuthorScope => edit is IAuthorScopedEdit { HasKnownAuthorScope: true };
        public string Description => edit.Description;
        public EditorText DisplayText => edit.DisplayText;
        public long RetainedBytes => edit.RetainedBytes;
        public void Redo()
        {
            if (_expected is { } expected && (!documents.IsLoaded || !ReferenceEquals(documents.CurrentDocument, _expectedDocument) || Capture(documents) != expected)) throw Stale();
            edit.Redo(); _expected = null; _expectedDocument = null;
        }
        public void Undo() => edit.Undo();
    }
}
