using System.Collections.ObjectModel;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Models.Graph;

namespace GalNet.Editor.Services;

/// <summary>Project-owned projections. Closing a Group page releases its subscriptions, not its content.</summary>
public sealed class EditorGroupProjectionStore(IEditorDocumentService documents, IEntryCatalog catalog) : IDisposable
{
    private readonly Dictionary<string, EditorGroupProjection> _groups = new(StringComparer.Ordinal);

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
        foreach (var group in _groups.Values) group.Dispose();
        _groups.Clear();
    }
}

public sealed class EditorGroupProjection : IDisposable
{
    public EditorGroupDocument Document { get; }
    public ObservableCollection<EntryEditorItemViewModel> Entries { get; }
    private readonly HashSet<EntryEditorItemViewModel> _allEntries = [];

    internal EditorGroupProjection(EditorGroupDocument document, IEntryCatalog catalog)
    {
        Document = document;
        Entries = new(document.Entries.Select(entry => new EntryEditorItemViewModel(entry, catalog)));
        _allEntries.UnionWith(Entries);
        Entries.CollectionChanged += OnEntriesChanged;
    }

    private void OnEntriesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    {
        _allEntries.UnionWith(Entries);
        Document.Entries.Clear();
        Document.Entries.AddRange(Entries.Select(entry => entry.Source));
    }

    public void Dispose()
    {
        Entries.CollectionChanged -= OnEntriesChanged;
        foreach (var entry in _allEntries) entry.Dispose();
        _allEntries.Clear();
    }
}
