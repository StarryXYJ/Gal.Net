using System.Collections.ObjectModel;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;

namespace GalNet.Editor.Services;

/// <summary>Project-owned projections. Closing a Group page releases its subscriptions, not its content.</summary>
public sealed class EditorGroupProjectionStore : IDisposable
{
    public IEnumerable<EditorGroupProjection> Loaded => _groups.Values;
    private readonly Dictionary<string, EditorGroupProjection> _groups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WeakReference<EditorGroupProjection>> _retired = new(StringComparer.Ordinal);
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
        if (scenes is not null) { scenes.Changed += RefreshSummaries; scenes.Text.PropertyChanged += OnLocale; }
    }
    public Exception? LastNotificationError { get; private set; }
    private void OnCommitted(AuthorChangeSet change)
    {
        if (change.UnknownScope) { Synchronize(); return; }
        foreach (var group in change.Groups)
            if (_groups.TryGetValue(group.GroupId, out var projection)) projection.Synchronize(catalog, group);
    }
    private void Synchronize()
    {
        foreach (var (id, group) in _groups.ToArray())
            if (documents.Groups.TryGetValue(id, out var source)) { group.Rebind(source); group.Synchronize(catalog); }
            else
            {
                group.Detach(); _groups.Remove(id);
                foreach (var key in _retired.Where(pair => !pair.Value.TryGetTarget(out _)).Select(pair => pair.Key).ToArray()) _retired.Remove(key);
                if (_retired.Count >= 64) _retired.Remove(_retired.Keys.First());
                _retired[id] = new(group);
            }
    }
    private void OnLocale(object? sender, System.ComponentModel.PropertyChangedEventArgs args) { if (args.PropertyName == "Item[]") RefreshSummaries(); }
    private void RefreshSummaries() { foreach (var group in _groups.Values) foreach (var entry in group.Entries) entry.RefreshSummary(); }

    public EditorGroupProjection Get(string groupId, string? sourcePath = null)
    {
        if (_groups.TryGetValue(groupId, out var existing)) return existing;
        var source = documents.GetOrCreateGroup(groupId, "Graph/" + (sourcePath ?? $"groups/{groupId}.rawgalgroup"));
        if (_retired.Remove(groupId, out var cached) && cached.TryGetTarget(out var restored))
        { restored.Rebind(source); restored.Synchronize(catalog); _groups.Add(groupId, restored); return restored; }
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
        if (scenes is not null) { scenes.Changed -= RefreshSummaries; scenes.Text.PropertyChanged -= OnLocale; }
        foreach (var group in _groups.Values) group.Dispose();
        foreach (var cached in _retired.Values) if (cached.TryGetTarget(out var group)) group.Dispose();
        _retired.Clear();
        _groups.Clear();
    }
}

public sealed class EditorGroupProjection : IDisposable
{
    public EditorGroupDocument Document { get; private set; }
    public ObservableCollection<EntryEditorItemViewModel> Entries { get; }
    private readonly List<WeakReference<EntryEditorItemViewModel>> _retiredEntries = [];
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
        foreach (var entry in Entries) Bind(entry);
    }
    internal void Rebind(EditorGroupDocument source) => Document = source;
    internal void Detach()
    {
        foreach (var entry in Entries.ToArray()) Retire(entry);
        Document = new(Document.Id, Document.RelativePath, []);
        _entries.Update(Entries.Clear);
    }
    private void Retire(EntryEditorItemViewModel entry)
    {
        try { entry.InvalidateAuthorBindings(); } catch (Exception failure) { _report(failure); }
        _retiredEntries.RemoveAll(reference => !reference.TryGetTarget(out _));
        if (_retiredEntries.Count >= 512) _retiredEntries.RemoveAt(0);
        _retiredEntries.Add(new(entry));
    }

    public void Synchronize(IEntryCatalog catalog, AuthorGroupChange? change = null)
    {
        if (Document.Entries.SequenceEqual(Entries.Select(entry => entry.Source)))
        {
            foreach (var entry in Entries.Where(entry => change is null || change.EntryIds.Contains(entry.StableId) || entry.Id != entry.Source.Id)) Refresh(entry, catalog);
            return;
        }
        var previous = Entries.ToArray();
        var current = Document.Entries.ToArray();
        foreach (var entry in previous.Where(entry => !current.Contains(entry.Source)))
            Retire(entry);
        var active = previous.ToDictionary(entry => entry.Source, entry => entry);
        _entries.Update(() =>
        {
            for (var index = 0; index < current.Length; index++)
            {
                var source = current[index];
                var entry = active.GetValueOrDefault(source) ?? _retiredEntries.Select(reference => reference.TryGetTarget(out var item) ? item : null)
                    .FirstOrDefault(item => ReferenceEquals(item?.Source, source)) ?? new EntryEditorItemViewModel(source, catalog);
                Bind(entry);
                if (Entries.Contains(entry)) { if (Entries.IndexOf(entry) != index) Entries.Move(Entries.IndexOf(entry), index); }
                else Entries.Insert(index, entry);
                Refresh(entry, catalog);
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
        foreach (var entry in Entries) entry.Dispose();
        foreach (var reference in _retiredEntries) if (reference.TryGetTarget(out var entry)) entry.Dispose();
        _retiredEntries.Clear();
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
