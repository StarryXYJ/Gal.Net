using System;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.Services;

public sealed class EditorDocumentService : IEditorDocumentService
{
    public EditorGraphDocument CurrentDocument { get; private set; } = new();
    public bool IsDirty { get; private set; }

    public event Action? DocumentChanged;
    public event Action<bool>? DirtyStateChanged;

    public void Load(LoadedEditorProjectDocument loadedDocument)
    {
        CurrentDocument = loadedDocument.Document ?? new EditorGraphDocument();
        IsDirty = false;
        DocumentChanged?.Invoke();
        DirtyStateChanged?.Invoke(false);
    }

    public void Unload()
    {
        CurrentDocument = new EditorGraphDocument();
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
}
