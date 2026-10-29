using System;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Abstraction.Services;

public interface IEditorDocumentService
{
    EditorGraphDocument CurrentDocument { get; }
    LoadedEditorProjectDocument LoadedProject { get; }
    IReadOnlyDictionary<string, EditorGroupDocument> Groups { get; }
    bool IsLoaded { get; }

    event Action? DocumentChanged;

    void Load(LoadedEditorProjectDocument loadedDocument);
    void Reload(LoadedEditorProjectDocument loadedDocument) => throw new NotSupportedException();
    void Unload();
    void SynchronizeGroups();
    EditorGroupDocument GetOrCreateGroup(string groupId, string relativePath);
    EditorGroupDocument? FindGroupByPath(string relativePath);
}
