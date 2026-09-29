using System;

namespace GalNet.Editor.History;

public sealed class EditorHistories
{
    public IUndoRedoHistory Graph { get; } = new UndoRedoHistory();
    public IUndoRedoHistory Ui { get; } = new UndoRedoHistory();
    public IUndoRedoHistory Settings { get; } = new UndoRedoHistory();

    public bool IsDirty => Graph.IsDirty || Ui.IsDirty || Settings.IsDirty;
    public event Action? Changed;

    public EditorHistories()
    {
        Graph.Changed += RaiseChanged;
        Ui.Changed += RaiseChanged;
        Settings.Changed += RaiseChanged;
    }

    public void Clear()
    {
        Graph.Clear();
        Ui.Clear();
        Settings.Clear();
    }

    public void MarkSaved()
    {
        Graph.MarkSaved();
        Ui.MarkSaved();
        Settings.MarkSaved();
    }

    private void RaiseChanged() => Changed?.Invoke();
}
