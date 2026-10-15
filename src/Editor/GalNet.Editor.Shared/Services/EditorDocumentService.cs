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
    public bool IsDirty { get; private set; }

    public event Action? DocumentChanged;
    public event Action<bool>? DirtyStateChanged;

    public void Load(LoadedEditorProjectDocument loadedDocument)
    {
        if (IsLoaded) throw new InvalidOperationException("The project document is already loaded.");
        LoadedProject = loadedDocument;
        foreach (var node in CurrentDocument.Nodes.Where(node => node.Type == "Group"))
            GetOrCreateGroup(node.Id, "Graph/" + (node.File ?? $"groups/{node.Id}.rawgalgroup"));
        IsLoaded = true;
        IsDirty = false;
        DocumentChanged?.Invoke();
        DirtyStateChanged?.Invoke(false);
    }

    public void Unload()
    {
        LoadedProject = new();
        _groups.Clear();
        _paths.Clear();
        IsLoaded = false;
        IsDirty = false;
        DocumentChanged?.Invoke();
        DirtyStateChanged?.Invoke(false);
    }

    public void MarkDirty()
    {
        if (IsDirty)
            return;

        IsDirty = true;
        DirtyStateChanged?.Invoke(true);
    }

    public void MarkSaved()
    {
        if (!IsDirty)
            return;

        IsDirty = false;
        DirtyStateChanged?.Invoke(false);
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
}
