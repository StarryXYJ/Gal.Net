using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Services;

namespace GalNet.Editor.History;

public interface IUndoableEdit
{
    long RetainedBytes => 0;
    string Description { get; }
    EditorText DisplayText => EditorText.History(Description);
    void Undo();
    void Redo();
}

public interface IUndoRedoHistory
{
    bool IsBusy { get; }
    bool RepairRequired { get; }
    bool CanUndo { get; }
    bool CanRedo { get; }
    bool IsDirty { get; }
    string? UndoDescription { get; }
    string? RedoDescription { get; }
    EditorText? UndoText => UndoDescription is { } text ? EditorText.History(text) : null;
    EditorText? RedoText => RedoDescription is { } text ? EditorText.History(text) : null;
    event Action? Changed;
    void Execute(IUndoableEdit edit);
    void Undo();
    void Redo();
    void Clear();
    void MarkSaved();
    Task ExecuteAsync(IAsyncUndoableEdit edit);
    Task ExecuteAsync(IUndoableEdit edit);
    Task UndoAsync();
    Task RedoAsync();
}

/// <summary>Each individual step must leave its input state intact when it throws.</summary>
public interface IAsyncUndoableEdit
{
    long RetainedBytes => 0;
    string Description { get; }
    EditorText DisplayText => EditorText.History(Description);
    ValueTask UndoAsync();
    ValueTask RedoAsync();
}

public interface IUndoRedoTarget
{
    IUndoRedoHistory? UndoRedoHistory { get; }
}

public sealed class UndoRedoHistory(int capacity = 200, long byteBudget = 1024L * 1024 * 1024) : IUndoRedoHistory, IDisposable
{
    public AuthorChangeJournal AuthorChanges { get; } = new();
    private readonly List<IAsyncUndoableEdit> _edits = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AsyncLocal<bool> _executing = new();
    private Func<bool>? _contentDirty;
    private Func<bool>? _contentRepair;
    private bool _suspended;
    private long _retainedBytes;
    private int _position;
    private int _savedPosition;
    private bool _savedPositionValid = true;

    public bool IsBusy { get; private set; }
    public bool RepairRequired { get; private set; }
    public Exception? LastCleanupError { get; private set; }
    public Exception? LastNotificationError { get; private set; }
    public bool CanUndo => !_suspended && !IsBusy && !RepairRequired && _contentRepair?.Invoke() != true && _position > 0;
    public bool CanRedo => !_suspended && !IsBusy && !RepairRequired && _contentRepair?.Invoke() != true && _position < _edits.Count;
    public bool IsDirty => _contentDirty?.Invoke() ?? (!_savedPositionValid || _position != _savedPosition);
    public string? UndoDescription => CanUndo ? _edits[_position - 1].Description : null;
    public string? RedoDescription => CanRedo ? _edits[_position].Description : null;
    public EditorText? UndoText => CanUndo ? _edits[_position - 1].DisplayText : null;
    public EditorText? RedoText => CanRedo ? _edits[_position].DisplayText : null;
    public event Action? Changed;

    public void Execute(IUndoableEdit edit)
    {
        RunSync(() => { edit.Redo(); Append(new SyncEdit(edit)); }, AuthorChangeCause.Execute, KnownScope(edit));
    }

    public void BindContent(Func<bool> dirty, Func<bool> repair)
    {
        _contentDirty = dirty;
        _contentRepair = repair;
    }

    private void Append(IAsyncUndoableEdit edit)
    {
        if (_position < _edits.Count)
        {
            if (_savedPosition > _position)
                _savedPositionValid = false;
            foreach (var removed in _edits.GetRange(_position, _edits.Count - _position)) Release(removed);
            _edits.RemoveRange(_position, _edits.Count - _position);
        }
        _edits.Add(edit);
        _position++;
        _retainedBytes = _edits.Sum(item => item.RetainedBytes);
        while (_edits.Count > 1 && (_edits.Count > Math.Max(1, capacity) || _retainedBytes > byteBudget))
        {
            _retainedBytes -= _edits[0].RetainedBytes;
            Release(_edits[0]);
            _edits.RemoveAt(0);
            _position--;
            if (_savedPosition > 0) _savedPosition--;
            else _savedPositionValid = false;
        }
    }

    public void Undo()
    {
        if (!CanUndo) return;
        RunSync(() => { RequireSync(_edits[_position - 1]).Undo(); _position--; }, AuthorChangeCause.Undo, KnownScope(_edits[_position - 1]));
    }

    public void Redo()
    {
        if (!CanRedo) return;
        RunSync(() => { RequireSync(_edits[_position]).Redo(); _position++; }, AuthorChangeCause.Redo, KnownScope(_edits[_position]));
    }

    private static IUndoableEdit RequireSync(IAsyncUndoableEdit edit) => edit is SyncEdit sync
        ? sync.Edit : throw new InvalidOperationException("Use the asynchronous history API for this edit.");

    internal static bool KnownScope(object edit) => edit is IAuthorScopedEdit { HasKnownAuthorScope: true };

    private void RunSync(Action operation, AuthorChangeCause cause, bool knownScope)
    {
        if (!_gate.Wait(0)) throw new InvalidOperationException("An edit is already executing.");
        AuthorChangeSet? committed = null;
        try
        {
            EnsureWritable(); _executing.Value = true; IsBusy = true; AuthorChanges.Begin();
            operation(); committed = AuthorChanges.Complete(cause, knownScope);
        }
        catch (HistoryRepairRequiredException) { RepairRequired = true; throw; }
        finally
        {
            try { AuthorChanges.Cancel(); }
            catch (HistoryRepairRequiredException) { RepairRequired = true; throw; }
            finally
            {
                IsBusy = false;
                // Publish with the new history cursor, while reentrant edits are still excluded.
                if (committed is not null) AuthorChanges.Publish(committed);
                _executing.Value = false; _gate.Release(); NotifyChanged();
            }
        }
    }

    private void EnsureWritable()
    {
        if (RepairRequired || _contentRepair?.Invoke() == true)
            throw new InvalidOperationException("Project repair is required before editing.");
        if (_suspended) throw new InvalidOperationException("Project edits are suspended while closing.");
    }

    public Task ExecuteAsync(IAsyncUndoableEdit edit) => RunAsync(async () =>
    {
        await edit.RedoAsync();
        Append(edit);
        return true;
    }, AuthorChangeCause.Execute, () => KnownScope(edit));

    public Task ExecuteAsync(IUndoableEdit edit) => ExecuteAsync(new SyncEdit(edit));

    public Task UndoAsync() => RunAsync(async () =>
    {
        if (_position == 0) return false;
        await _edits[_position - 1].UndoAsync();
        _position--;
        return true;
    }, AuthorChangeCause.Undo, () => _position > 0 && KnownScope(_edits[_position - 1]));

    public Task RedoAsync() => RunAsync(async () =>
    {
        if (_position == _edits.Count) return false;
        await _edits[_position].RedoAsync();
        _position++;
        return true;
    }, AuthorChangeCause.Redo, () => _position < _edits.Count && KnownScope(_edits[_position]));

    private async Task RunAsync(Func<Task<bool>> operation, AuthorChangeCause cause, Func<bool> scope)
    {
        if (_executing.Value) throw new InvalidOperationException("History operations cannot reenter history.");
        await _gate.WaitAsync();
        AuthorChangeSet? committed = null;
        try
        {
            EnsureWritable();
            _executing.Value = true;
            IsBusy = true;
            AuthorChanges.Begin();
            var knownScope = scope();
            NotifyChanged();
            if (await operation()) committed = AuthorChanges.Complete(cause, knownScope);
        }
        catch (HistoryRepairRequiredException) { RepairRequired = true; throw; }
        finally
        {
            try { AuthorChanges.Cancel(); }
            catch (HistoryRepairRequiredException) { RepairRequired = true; throw; }
            finally
            {
                IsBusy = false;
                if (committed is not null) AuthorChanges.Publish(committed);
                _executing.Value = false; _gate.Release(); NotifyChanged();
            }
        }
    }

    private sealed record SyncEdit(IUndoableEdit Edit) : IAsyncUndoableEdit, IDisposable, IAuthorScopedEdit
    {
        public bool HasKnownAuthorScope => KnownScope(Edit);
        public string Description => Edit.Description;
        public EditorText DisplayText => Edit.DisplayText;
        public long RetainedBytes => Edit.RetainedBytes;
        public ValueTask UndoAsync() { Edit.Undo(); return ValueTask.CompletedTask; }
        public ValueTask RedoAsync() { Edit.Redo(); return ValueTask.CompletedTask; }
        public void Dispose() => (Edit as IDisposable)?.Dispose();
    }

    public async Task SuspendAsync()
    {
        _suspended = true;
        await _gate.WaitAsync();
        _gate.Release();
        NotifyChanged();
    }

    public void Resume() { _suspended = false; NotifyChanged(); }

    public async Task<IDisposable> AcquireCaptureAsync(bool allowExecutingEdit = false, CancellationToken cancellationToken = default)
    {
        if (_executing.Value)
        {
            if (allowExecutingEdit) return new EmptyLease();
            throw new InvalidOperationException("Cannot capture a partially executing edit.");
        }
        await _gate.WaitAsync(cancellationToken);
        return new CaptureLease(_gate);
    }

    private sealed class EmptyLease : IDisposable { public void Dispose() { } }

    private sealed class CaptureLease(SemaphoreSlim gate) : IDisposable
    {
        private bool _disposed;
        public void Dispose() { if (_disposed) return; _disposed = true; gate.Release(); }
    }

    public void Dispose()
    {
        foreach (var edit in _edits) Release(edit);
        _edits.Clear();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Clear()
    {
        if (IsBusy || _executing.Value) throw new InvalidOperationException("Cannot clear executing history.");
        foreach (var edit in _edits) Release(edit);
        _edits.Clear();
        _position = 0;
        _savedPosition = 0;
        _savedPositionValid = true;
        NotifyChanged();
    }

    public void MarkSaved()
    {
        _savedPosition = _position;
        _savedPositionValid = true;
        NotifyChanged();
    }

    private void Release(IAsyncUndoableEdit edit)
    {
        try { (edit as IDisposable)?.Dispose(); }
        catch (Exception failure) { LastCleanupError = failure; }
    }

    private void NotifyChanged()
    {
        if (Changed is null) return;
        foreach (Action observer in Changed.GetInvocationList())
            try { observer(); }
            catch (Exception failure) { LastNotificationError = failure; Serilog.Log.Error(failure, "A history observer failed"); }
    }
}

public sealed class HistoryRepairRequiredException(string message, Exception innerException) : Exception(message, innerException);

public sealed class AsyncCompositeEdit(string description, IReadOnlyList<IAsyncUndoableEdit> edits, EditorText? display = null) : IAsyncUndoableEdit, IDisposable, IAuthorScopedEdit
{
    public bool HasKnownAuthorScope => edits.All(UndoRedoHistory.KnownScope);
    public string Description { get; } = description;
    public EditorText DisplayText => display ?? EditorText.History(Description);
    public long RetainedBytes => edits.Sum(edit => edit.RetainedBytes);
    public void Dispose()
    {
        List<Exception> failures = [];
        foreach (var edit in edits)
            try { (edit as IDisposable)?.Dispose(); }
            catch (Exception failure) { failures.Add(failure); }
        if (failures.Count > 0) throw new AggregateException("History resource cleanup failed.", failures);
    }

    public async ValueTask RedoAsync()
    {
        var index = 0;
        try { for (; index < edits.Count; index++) await edits[index].RedoAsync(); }
        catch (Exception failure)
        {
            try { for (var restore = index - 1; restore >= 0; restore--) await edits[restore].UndoAsync(); }
            catch (Exception compensation) { throw new HistoryRepairRequiredException("Edit compensation failed.", new AggregateException(failure, compensation)); }
            throw;
        }
    }

    public async ValueTask UndoAsync()
    {
        var index = edits.Count - 1;
        try { for (; index >= 0; index--) await edits[index].UndoAsync(); }
        catch (Exception failure)
        {
            try { for (var restore = index + 1; restore < edits.Count; restore++) await edits[restore].RedoAsync(); }
            catch (Exception compensation) { throw new HistoryRepairRequiredException("Undo compensation failed.", new AggregateException(failure, compensation)); }
            throw;
        }
    }
}

public sealed class AsyncEditAdapter(IUndoableEdit edit) : IAsyncUndoableEdit, IAuthorScopedEdit
{
    public bool HasKnownAuthorScope => UndoRedoHistory.KnownScope(edit);
    public string Description => edit.Description;
    public EditorText DisplayText => edit.DisplayText;
    public long RetainedBytes => edit.RetainedBytes;
    public ValueTask RedoAsync() { edit.Redo(); return ValueTask.CompletedTask; }
    public ValueTask UndoAsync() { edit.Undo(); return ValueTask.CompletedTask; }
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

public sealed class CompositeEdit(string description, IReadOnlyList<IUndoableEdit> edits) : IUndoableEdit, IDisposable, IAuthorScopedEdit
{
    public bool HasKnownAuthorScope => edits.All(UndoRedoHistory.KnownScope);
    public string Description { get; } = description;
    public long RetainedBytes => edits.Sum(edit => edit.RetainedBytes);
    public void Dispose()
    {
        List<Exception> failures = [];
        foreach (var edit in edits)
            try { (edit as IDisposable)?.Dispose(); }
            catch (Exception failure) { failures.Add(failure); }
        if (failures.Count > 0) throw new AggregateException("History resource cleanup failed.", failures);
    }
    public void Undo()
    {
        var index = edits.Count - 1;
        try { for (; index >= 0; index--) edits[index].Undo(); }
        catch (Exception failure)
        {
            try { for (var restore = index + 1; restore < edits.Count; restore++) edits[restore].Redo(); }
            catch (Exception compensation) { throw new HistoryRepairRequiredException("Undo compensation failed.", new AggregateException(failure, compensation)); }
            throw;
        }
    }
    public void Redo()
    {
        var index = 0;
        try { for (; index < edits.Count; index++) edits[index].Redo(); }
        catch (Exception failure)
        {
            try { for (var restore = index - 1; restore >= 0; restore--) edits[restore].Undo(); }
            catch (Exception compensation) { throw new HistoryRepairRequiredException("Edit compensation failed.", new AggregateException(failure, compensation)); }
            throw;
        }
    }
}

public sealed class UndoRedoRouter(EditorHistories histories, GalNet.Editor.Abstraction.Services.IProjectContentSession? session = null)
{
    public object? ActiveTarget { get; private set; }
    public IUndoRedoHistory Active => histories.Content;
    public event Action? Changed
    {
        add { Active.Changed += value; if (session is not null) session.Changed += value; }
        remove { Active.Changed -= value; if (session is not null) session.Changed -= value; }
    }

    public void SetActive(object? target)
    {
        ActiveTarget = target;
    }

    public Task UndoAsync() => RunAsync(Active.UndoAsync);
    public Task RedoAsync() => RunAsync(Active.RedoAsync);
    private async Task RunAsync(Func<Task> operation)
    {
        try { if (session is not null) await session.CommitDraftsAsync(); await operation(); }
        catch (Exception failure) { session?.ReportError(failure); throw; }
    }
}
