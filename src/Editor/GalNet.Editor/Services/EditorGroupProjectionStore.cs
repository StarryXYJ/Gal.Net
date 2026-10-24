using System.Collections.ObjectModel;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Models.Graph;

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
    private readonly History.EditorHistories? histories;
    private readonly EntryEditSession? edits;
    public EditorGroupProjectionStore(IEditorDocumentService documents, IEntryCatalog catalog, EntryFieldAuthoringService authoring, SceneTargetCatalog? scenes = null, History.EditorHistories? histories = null, EntryEditSession? edits = null)
    {
        this.documents = documents;
        this.catalog = catalog;
        this.authoring = authoring;
        this.scenes = scenes; this.histories = histories; this.edits = edits;
        documents.DocumentChanged += Synchronize;
        authoring.Changed += RefreshFields;
        if (histories is not null) histories.Changed += Synchronize;
    }
    private void RefreshFields(string groupId) { if (_groups.TryGetValue(groupId, out var group)) group.Synchronize(catalog); }
    private void Synchronize()
    {
        foreach (var group in _groups.Values) group.Synchronize(catalog);
    }

    public EditorGroupProjection Get(string groupId, string? sourcePath = null)
    {
        if (_groups.TryGetValue(groupId, out var existing)) return existing;
        var source = documents.GetOrCreateGroup(groupId, "Graph/" + (sourcePath ?? $"groups/{groupId}.rawgalgroup"));
        var projection = new EditorGroupProjection(source, catalog, authoring, scenes, edits);
        _groups.Add(groupId, projection);
        return projection;
    }

    public void Dispose()
    {
        documents.DocumentChanged -= Synchronize;
        authoring.Changed -= RefreshFields;
        if (histories is not null) histories.Changed -= Synchronize;
        foreach (var group in _groups.Values) group.Dispose();
        _groups.Clear();
    }
}

public sealed class EditorGroupProjection : IDisposable
{
    public EditorGroupDocument Document { get; }
    public ObservableCollection<EntryEditorItemViewModel> Entries { get; }
    private readonly HashSet<EntryEditorItemViewModel> _allEntries = [];
    private bool _synchronizing;
    private readonly EntryFieldAuthoringService _authoring;
    private readonly SceneTargetCatalog? _scenes;
    private readonly EntryEditSession? _edits;

    internal EditorGroupProjection(EditorGroupDocument document, IEntryCatalog catalog, EntryFieldAuthoringService authoring, SceneTargetCatalog? scenes, EntryEditSession? edits = null)
    {
        Document = document;
        _authoring = authoring;
        _scenes = scenes; _edits = edits;
        Entries = new(document.Entries.Select(entry => new EntryEditorItemViewModel(entry, catalog)));
        _allEntries.UnionWith(Entries);
        foreach (var entry in Entries) Bind(entry);
        Entries.CollectionChanged += OnEntriesChanged;
    }

    private void OnEntriesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    {
        if (_synchronizing) return;
        if (args.OldItems is not null)
            foreach (EntryEditorItemViewModel entry in args.OldItems)
                if (!Entries.Contains(entry)) entry.InvalidateParameterFields();
        _allEntries.UnionWith(Entries);
        foreach (var entry in Entries) Bind(entry);
        Document.Entries.Clear();
        Document.Entries.AddRange(Entries.Select(entry => entry.Source));
    }

    public void Synchronize(IEntryCatalog catalog)
    {
        if (Document.Entries.SequenceEqual(Entries.Select(entry => entry.Source)))
        {
            foreach (var entry in Entries) entry.RefreshAuthorReferences(catalog);
            return;
        }
        var previous = Entries.ToArray();
        var current = Document.Entries.ToArray();
        foreach (var entry in previous.Where(entry => !current.Contains(entry.Source))) entry.InvalidateParameterFields();
        _synchronizing = true;
        try
        {
            Entries.Clear();
            foreach (var source in current)
            {
                var entry = previous.FirstOrDefault(entry => ReferenceEquals(entry.Source, source)) ?? new EntryEditorItemViewModel(source, catalog);
                Entries.Add(entry);
                Bind(entry);
                _allEntries.Add(entry);
            }
        }
        finally { _synchronizing = false; }
    }

    private void Bind(EntryEditorItemViewModel entry) { entry.EditSession = _edits; entry.BindAuthoring(Document.Id, _authoring, _scenes); }

    public void Dispose()
    {
        Entries.CollectionChanged -= OnEntriesChanged;
        foreach (var entry in _allEntries) entry.Dispose();
        _allEntries.Clear();
    }
}
