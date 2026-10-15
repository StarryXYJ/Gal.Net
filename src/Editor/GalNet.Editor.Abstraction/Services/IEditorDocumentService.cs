using System;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Abstraction.Services;

public interface IEditorDocumentService
{
    EditorGraphDocument CurrentDocument { get; }
    LoadedEditorProjectDocument LoadedProject { get; }
    IReadOnlyDictionary<string, EditorGroupDocument> Groups { get; }
    bool IsLoaded { get; }
    bool IsDirty { get; }

    event Action? DocumentChanged;
    event Action<bool>? DirtyStateChanged;

    void Load(LoadedEditorProjectDocument loadedDocument);
    void Unload();
    void MarkDirty();
    void MarkSaved();
    EditorGroupDocument GetOrCreateGroup(string groupId, string relativePath);
    EditorGroupDocument? FindGroupByPath(string relativePath);
}
