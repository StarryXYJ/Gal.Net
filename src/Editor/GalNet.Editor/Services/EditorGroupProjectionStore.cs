using System.Collections.ObjectModel;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Models.Graph;

namespace GalNet.Editor.Services;

/// <summary>Project-owned projections. Closing a Group page releases its subscriptions, not its content.</summary>
public sealed class EditorGroupProjectionStore : IDisposable
{
    private readonly Dictionary<string, EditorGroupProjection> _groups = new(StringComparer.Ordinal);
    private readonly IEditorDocumentService documents;
    private readonly IEntryCatalog catalog;
    public EditorGroupProjectionStore(IEditorDocumentService documents, IEntryCatalog catalog)
    {
        this.documents = documents;
        this.catalog = catalog;
        documents.DocumentChanged += Synchronize;
    }
    private void Synchronize()
    {
        foreach (var group in _groups.Values) group.Synchronize(catalog);
    }

    public EditorGroupProjection Get(string groupId, string? sourcePath = null)
    {
        if (_groups.TryGetValue(groupId, out var existing)) return existing;
        var source = documents.GetOrCreateGroup(groupId, "Graph/" + (sourcePath ?? $"groups/{groupId}.rawgalgroup"));
        var projection = new EditorGroupProjection(source, catalog);
        _groups.Add(groupId, projection);
        return projection;
    }

    public void Dispose()
    {
        documents.DocumentChanged -= Synchronize;
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

    internal EditorGroupProjection(EditorGroupDocument document, IEntryCatalog catalog)
    {
        Document = document;
        Entries = new(document.Entries.Select(entry => new EntryEditorItemViewModel(entry, catalog)));
        _allEntries.UnionWith(Entries);
        Entries.CollectionChanged += OnEntriesChanged;
    }

    private void OnEntriesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    {
        if (_synchronizing) return;
        _allEntries.UnionWith(Entries);
        Document.Entries.Clear();
        Document.Entries.AddRange(Entries.Select(entry => entry.Source));
    }

    public void Synchronize(IEntryCatalog catalog)
    {
        if (Document.Entries.SequenceEqual(Entries.Select(entry => entry.Source))) return;
        var previous = Entries.ToArray();
        var current = Document.Entries.ToArray();
        _synchronizing = true;
        try
        {
            Entries.Clear();
            foreach (var source in current)
            {
                var entry = previous.FirstOrDefault(entry => ReferenceEquals(entry.Source, source)) ?? new EntryEditorItemViewModel(source, catalog);
                Entries.Add(entry);
                _allEntries.Add(entry);
            }
        }
        finally { _synchronizing = false; }
    }

    public void Dispose()
    {
        Entries.CollectionChanged -= OnEntriesChanged;
        foreach (var entry in _allEntries) entry.Dispose();
        _allEntries.Clear();
    }
}
