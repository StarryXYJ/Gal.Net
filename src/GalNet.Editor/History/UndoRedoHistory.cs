using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GalNet.Editor.History;

public interface IUndoableEdit
{
    string Description { get; }
    void Undo();
    void Redo();
}

public interface IUndoRedoHistory
{
    bool CanUndo { get; }
    bool CanRedo { get; }
    bool IsDirty { get; }
    string? UndoDescription { get; }
    string? RedoDescription { get; }
    event Action? Changed;
    void Execute(IUndoableEdit edit);
    void PushAlreadyApplied(IUndoableEdit edit);
    void Undo();
    void Redo();
    void Clear();
    void MarkSaved();
}

public interface IUndoRedoTarget
{
    IUndoRedoHistory? UndoRedoHistory { get; }
}

public sealed class UndoRedoHistory(int capacity = 200) : IUndoRedoHistory
{
    private readonly List<IUndoableEdit> _edits = [];
    private int _position;
    private int _savedPosition;
    private bool _savedPositionValid = true;

    public bool CanUndo => _position > 0;
    public bool CanRedo => _position < _edits.Count;
    public bool IsDirty => !_savedPositionValid || _position != _savedPosition;
    public string? UndoDescription => CanUndo ? _edits[_position - 1].Description : null;
    public string? RedoDescription => CanRedo ? _edits[_position].Description : null;
    public event Action? Changed;

    public void Execute(IUndoableEdit edit)
    {
        edit.Redo();
        PushAlreadyApplied(edit);
    }

    public void PushAlreadyApplied(IUndoableEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (_position < _edits.Count)
        {
            if (_savedPosition > _position)
                _savedPositionValid = false;
            _edits.RemoveRange(_position, _edits.Count - _position);
        }
        _edits.Add(edit);
        _position++;
        if (_edits.Count > capacity)
        {
            _edits.RemoveAt(0);
            _position--;
            if (_savedPosition > 0) _savedPosition--;
            else _savedPositionValid = false;
        }
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        _edits[--_position].Undo();
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        _edits[_position++].Redo();
        Changed?.Invoke();
    }

    public void Clear()
    {
        _edits.Clear();
        _position = 0;
        _savedPosition = 0;
        _savedPositionValid = true;
        Changed?.Invoke();
    }

    public void MarkSaved()
    {
        _savedPosition = _position;
        _savedPositionValid = true;
        Changed?.Invoke();
    }
}

public sealed class DelegateEdit(string description, Action undo, Action redo) : IUndoableEdit
{
    public string Description { get; } = description;
    public void Undo() => undo();
    public void Redo() => redo();
}

public sealed class PropertyEdit<T>(string description, Action<T> setter, T before, T after) : IUndoableEdit
{
    public string Description { get; } = description;
    public void Undo() => setter(before);
    public void Redo() => setter(after);
}

public sealed class CollectionInsertEdit<T>(string description, ObservableCollection<T> collection, T item, int index) : IUndoableEdit
{
    public string Description { get; } = description;
    public void Undo() => collection.Remove(item);
    public void Redo() => collection.Insert(Math.Clamp(index, 0, collection.Count), item);
}

public sealed class CollectionRemoveEdit<T>(string description, ObservableCollection<T> collection, T item, int index) : IUndoableEdit
{
    public string Description { get; } = description;
    public void Undo() => collection.Insert(Math.Clamp(index, 0, collection.Count), item);
    public void Redo() => collection.Remove(item);
}

public sealed class CollectionMoveEdit<T>(string description, ObservableCollection<T> collection, int before, int after) : IUndoableEdit
{
    public string Description { get; } = description;
    public void Undo() => collection.Move(after, before);
    public void Redo() => collection.Move(before, after);
}

public sealed class CompositeEdit(string description, IReadOnlyList<IUndoableEdit> edits) : IUndoableEdit
{
    public string Description { get; } = description;
    public void Undo()
    {
        for (var index = edits.Count - 1; index >= 0; index--) edits[index].Undo();
    }
    public void Redo()
    {
        foreach (var edit in edits) edit.Redo();
    }
}

public sealed class UndoRedoRouter
{
    private IUndoRedoHistory? _active;
    public IUndoRedoHistory? Active => _active;
    public event Action? Changed;

    public void SetActive(object? target)
    {
        var next = (target as IUndoRedoTarget)?.UndoRedoHistory;
        if (ReferenceEquals(next, _active)) return;
        if (_active is not null) _active.Changed -= OnHistoryChanged;
        _active = next;
        if (_active is not null) _active.Changed += OnHistoryChanged;
        Changed?.Invoke();
    }

    public void Undo() => _active?.Undo();
    public void Redo() => _active?.Redo();
    private void OnHistoryChanged() => Changed?.Invoke();
}
