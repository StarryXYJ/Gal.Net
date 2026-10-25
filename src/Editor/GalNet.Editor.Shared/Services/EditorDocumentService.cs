using System;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.Shared.Services;

public sealed class EditorDocumentService : IEditorDocumentService
{
    private readonly Dictionary<string, EditorGroupDocument> _groups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EditorGroupDocument> _paths = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    public LoadedEditorProjectDocument LoadedProject { get; private set; } = new();
    public EditorGraphDocument CurrentDocument => LoadedProject.Document;
    public IReadOnlyDictionary<string, EditorGroupDocument> Groups => _groups;
    public bool IsLoaded { get; private set; }

    public event Action? DocumentChanged;

    public void Load(LoadedEditorProjectDocument loadedDocument)
    {
        if (IsLoaded) throw new InvalidOperationException("The project document is already loaded.");
        LoadedProject = loadedDocument;
        foreach (var node in CurrentDocument.Nodes.Where(node => node.Type == "Group"))
            GetOrCreateGroup(node.Id, "Graph/" + (node.File ?? $"groups/{node.Id}.rawgalgroup"));
        IsLoaded = true;
        NotifyDocumentChanged();
    }

    public void Unload()
    {
        LoadedProject = new();
        _groups.Clear();
        _paths.Clear();
        IsLoaded = false;
        NotifyDocumentChanged();
    }

    public void Reload(LoadedEditorProjectDocument loadedDocument)
    {
        if (!IsLoaded) throw new InvalidOperationException("No project document is loaded.");
        var next = loadedDocument.Document;
        var old = CurrentDocument;
        // Keep DTO and Group identities where their author identity and kind still match.
        next.Nodes = next.Nodes.Select(node =>
        {
            var existing = old.Nodes.SingleOrDefault(item => item.Id == node.Id && item.Type == node.Type && item.BranchType == node.BranchType);
            if (existing is null) return node;
            existing.Name = node.Name; existing.X = node.X; existing.Y = node.Y; existing.File = node.File;
            existing.Options = node.Options; existing.Conditions = node.Conditions;
            return existing;
        }).ToList();
        foreach (var (id, entries) in loadedDocument.GroupEntries)
            if (LoadedProject.GroupEntries.TryGetValue(id, out var previous))
                for (var index = 0; index < entries.Count; index++)
                {
                    var entry = entries[index];
                    var existing = previous.SingleOrDefault(item => item.StableId == entry.StableId && item.Type == entry.Type);
                    if (existing is null) continue;
                    existing.Id = entry.Id; existing.Condition = entry.Condition; existing.Parameters = entry.Parameters;
                    entries[index] = existing;
                }
        old.Version = next.Version; old.Name = next.Name; old.RootNodeId = next.RootNodeId;
        old.Nodes = next.Nodes; old.Edges = next.Edges;
        old.PlayerVariables = next.PlayerVariables; old.SaveVariables = next.SaveVariables;
        LoadedProject.GroupEntries = loadedDocument.GroupEntries;
        foreach (var id in _groups.Keys.Where(id => !loadedDocument.GroupEntries.ContainsKey(id)).ToArray())
            _groups[id].Entries.Clear();
        SynchronizeGroups();
    }

    public EditorGroupDocument GetOrCreateGroup(string groupId, string relativePath)
    {
        if (_groups.TryGetValue(groupId, out var existing)) return existing;
        relativePath = relativePath.Replace('\\', '/');
        if (_paths.ContainsKey(relativePath)) throw new InvalidDataException($"A Group document already owns '{relativePath}'.");
        if (!LoadedProject.GroupEntries.TryGetValue(groupId, out var entries))
            LoadedProject.GroupEntries.Add(groupId, entries = []);
        var document = new EditorGroupDocument(groupId, relativePath, entries);
        _groups.Add(groupId, document);
        _paths.Add(relativePath, document);
        return document;
    }

    public EditorGroupDocument? FindGroupByPath(string relativePath) => _paths.GetValueOrDefault(relativePath.Replace('\\', '/'));

    public void SynchronizeGroups()
    {
        _paths.Clear();
        foreach (var node in CurrentDocument.Nodes.Where(node => node.Type == "Group"))
        {
            var path = ("Graph/" + node.File).Replace('\\', '/');
            if (_groups.TryGetValue(node.Id, out var existing))
            {
                if (LoadedProject.GroupEntries.TryGetValue(node.Id, out var entries) && !ReferenceEquals(entries, existing.Entries))
                {
                    existing.Entries.Clear();
                    existing.Entries.AddRange(entries);
                    LoadedProject.GroupEntries[node.Id] = existing.Entries;
                }
                existing.ChangeSourcePath(path);
                _paths.Add(path, existing);
            }
            else GetOrCreateGroup(node.Id, path);
        }
        NotifyDocumentChanged();
    }

    private void NotifyDocumentChanged()
    {
        if (DocumentChanged is null) return;
        foreach (Action observer in DocumentChanged.GetInvocationList())
            try { observer(); }
            catch (Exception failure) { Serilog.Log.Error(failure, "An author document observer failed"); }
    }
}
