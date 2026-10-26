using System.Collections.ObjectModel;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.History;

namespace GalNet.Editor.Services;

/// <summary>Project-owned projections. Closing a Group page releases its subscriptions, not its content.</summary>
public sealed class EditorGroupProjectionStore : IDisposable
{
    public IEnumerable<EditorGroupProjection> Loaded => _groups.Values;
    private readonly Dictionary<string, EditorGroupProjection> _groups = new(StringComparer.Ordinal);
    private readonly IEditorDocumentService documents;
    private readonly IEntryCatalog catalog;
    private readonly EntryFieldAuthoringService authoring;
    private readonly SceneTargetCatalog? scenes;
    private readonly AuthorChangeJournal changes;
    private readonly EntryEditSession? edits;
    public EditorGroupProjectionStore(IEditorDocumentService documents, IEntryCatalog catalog, EntryFieldAuthoringService authoring, SceneTargetCatalog? scenes = null, History.EditorHistories? histories = null, EntryEditSession? edits = null)
    {
        this.documents = documents;
        this.catalog = catalog;
        this.authoring = authoring;
        this.scenes = scenes; this.changes = histories?.Content.AuthorChanges ?? authoring.Changes; this.edits = edits;
        documents.DocumentChanged += Synchronize;
        changes.Committed += OnCommitted;
    }
    public Exception? LastNotificationError { get; private set; }
    private void OnCommitted(AuthorChangeSet _) => Synchronize();
    private void Synchronize()
    {
        foreach (var group in _groups.Values.ToArray()) group.Synchronize(catalog);
    }

    public EditorGroupProjection Get(string groupId, string? sourcePath = null)
    {
        if (_groups.TryGetValue(groupId, out var existing)) return existing;
        var source = documents.GetOrCreateGroup(groupId, "Graph/" + (sourcePath ?? $"groups/{groupId}.rawgalgroup"));
        var projection = new EditorGroupProjection(source, catalog, authoring, scenes, edits, failure =>
        {
            LastNotificationError = failure;
            Serilog.Log.Error(failure, "A Group projection observer failed for {GroupId}", groupId);
        });
        _groups.Add(groupId, projection);
        return projection;
    }

    public void Dispose()
    {
        documents.DocumentChanged -= Synchronize;
        changes.Committed -= OnCommitted;
        foreach (var group in _groups.Values) group.Dispose();
        _groups.Clear();
    }
}

public sealed class EditorGroupProjection : IDisposable
{
    public EditorGroupDocument Document { get; }
    public ObservableCollection<EntryEditorItemViewModel> Entries { get; }
    private readonly HashSet<EntryEditorItemViewModel> _allEntries = [];
    private readonly ProjectionEntryCollection _entries;
    private readonly EntryFieldAuthoringService _authoring;
    private readonly SceneTargetCatalog? _scenes;
    private readonly EntryEditSession? _edits;
    private readonly Action<Exception> _report;

    internal EditorGroupProjection(EditorGroupDocument document, IEntryCatalog catalog, EntryFieldAuthoringService authoring, SceneTargetCatalog? scenes, EntryEditSession? edits, Action<Exception> report)
    {
        Document = document;
        _authoring = authoring;
        _scenes = scenes; _edits = edits;
        _report = report;
        _entries = new(document.Entries.Select(entry => new EntryEditorItemViewModel(entry, catalog)), report);
        Entries = _entries;
        _allEntries.UnionWith(Entries);
        foreach (var entry in Entries) Bind(entry);
    }

    public void Synchronize(IEntryCatalog catalog)
    {
        if (Document.Entries.SequenceEqual(Entries.Select(entry => entry.Source)))
        {
            foreach (var entry in Entries) Refresh(entry, catalog);
            return;
        }
        var previous = Entries.ToArray();
        var current = Document.Entries.ToArray();
        foreach (var entry in previous.Where(entry => !current.Contains(entry.Source)))
            try { entry.InvalidateParameterFields(); } catch (Exception failure) { _report(failure); }
        _entries.Update(() =>
        {
            for (var index = 0; index < current.Length; index++)
            {
                var source = current[index];
                var entry = _allEntries.FirstOrDefault(entry => ReferenceEquals(entry.Source, source)) ?? new EntryEditorItemViewModel(source, catalog);
                Bind(entry);
                if (Entries.Contains(entry)) { if (Entries.IndexOf(entry) != index) Entries.Move(Entries.IndexOf(entry), index); }
                else Entries.Insert(index, entry);
                Refresh(entry, catalog);
                _allEntries.Add(entry);
            }
            while (Entries.Count > current.Length) Entries.RemoveAt(Entries.Count - 1);
        });
    }

    private void Refresh(EntryEditorItemViewModel entry, IEntryCatalog catalog)
    {
        try { entry.RefreshAuthorReferences(catalog); }
        catch (Exception failure) { _report(failure); }
    }

    private void Bind(EntryEditorItemViewModel entry) { entry.EditSession = _edits; entry.BindAuthoring(Document.Id, _authoring, _scenes); }

    public void Dispose()
    {
        foreach (var entry in _allEntries) entry.Dispose();
        _allEntries.Clear();
    }
}

/// <summary>Read-only to consumers. One failed UI subscriber cannot interrupt projection reconciliation.</summary>
internal sealed class ProjectionEntryCollection(IEnumerable<EntryEditorItemViewModel> initial, Action<Exception> report)
    : ObservableCollection<EntryEditorItemViewModel>(initial)
{
    private bool _updating;
    private bool _notifying;
    public void Update(Action update)
    {
        _updating = true;
        try { update(); }
        finally { _updating = false; }
    }
    private void EnsureUpdating() { if (!_updating || _notifying) throw new InvalidOperationException("Edit Group entries through the authoring service."); }
    protected override void InsertItem(int index, EntryEditorItemViewModel item) { EnsureUpdating(); base.InsertItem(index, item); }
    protected override void RemoveItem(int index) { EnsureUpdating(); base.RemoveItem(index); }
    protected override void SetItem(int index, EntryEditorItemViewModel item) { EnsureUpdating(); base.SetItem(index, item); }
    protected override void MoveItem(int oldIndex, int newIndex) { EnsureUpdating(); base.MoveItem(oldIndex, newIndex); }
    protected override void ClearItems() { EnsureUpdating(); base.ClearItems(); }
    public override event System.Collections.Specialized.NotifyCollectionChangedEventHandler? CollectionChanged;
    protected override event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected override void OnCollectionChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    {
        if (CollectionChanged is null) return;
        _notifying = true;
        try
        {
            foreach (System.Collections.Specialized.NotifyCollectionChangedEventHandler observer in CollectionChanged.GetInvocationList())
                try { observer(this, args); } catch (Exception failure) { report(failure); }
        }
        finally { _notifying = false; }
    }
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (PropertyChanged is null) return;
        _notifying = true;
        try
        {
            foreach (System.ComponentModel.PropertyChangedEventHandler observer in PropertyChanged.GetInvocationList())
                try { observer(this, args); } catch (Exception failure) { report(failure); }
        }
        finally { _notifying = false; }
    }
}
