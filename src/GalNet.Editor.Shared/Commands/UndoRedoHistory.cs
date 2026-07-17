using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Commands;

namespace GalNet.Editor.Shared.Commands;

internal sealed class UndoRedoHistory
{
    private const int MaxEntries = 200;
    private readonly List<HistoryEntry> _entries = [];
    private int _position;
    private int _savedPosition;

    public bool CanUndo => _position > 0;
    public bool CanRedo => _position < _entries.Count;
    public bool IsDirty => _savedPosition < 0 || _position != _savedPosition;
    public string? UndoDescription => CanUndo ? _entries[_position - 1].Description.Description : null;
    public string? RedoDescription => CanRedo ? _entries[_position].Description.Description : null;

    public void Push(
        IEditorChange change,
        EditorExecutionDescription description,
        string? mergeKey = null,
        TimeSpan? mergeWindow = null)
    {
        if (_position < _entries.Count)
        {
            _entries.RemoveRange(_position, _entries.Count - _position);
            if (_savedPosition > _position)
                _savedPosition = -1;
        }

        var now = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(mergeKey)
            && _position == _entries.Count
            && _position > 0
            && _savedPosition != _position
            && _entries[^1] is { } previous
            && string.Equals(previous.MergeKey, mergeKey, StringComparison.Ordinal)
            && now - previous.Timestamp <= (mergeWindow ?? TimeSpan.FromMilliseconds(750)))
        {
            var mergedResources = previous.Change.ChangedResources
                .Concat(change.ChangedResources)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var mergedChange = new EditorDocumentChange(
                previous.Change.Revert(),
                change.Apply(),
                mergedResources);
            _entries[^1] = new HistoryEntry(mergedChange, description, mergeKey, now);
            return;
        }

        _entries.Add(new HistoryEntry(change, description, mergeKey, now));
        _position++;
        Trim();
    }

    public HistoryEntry Undo()
    {
        if (!CanUndo) throw new InvalidOperationException("There is no command to undo.");
        return _entries[--_position];
    }

    public HistoryEntry Redo()
    {
        if (!CanRedo) throw new InvalidOperationException("There is no command to redo.");
        return _entries[_position++];
    }

    public void MarkSaved() => _savedPosition = _position;

    private void Trim()
    {
        var removeCount = _entries.Count - MaxEntries;
        if (removeCount <= 0) return;
        _entries.RemoveRange(0, removeCount);
        _position -= removeCount;
        _savedPosition = _savedPosition >= removeCount ? _savedPosition - removeCount : -1;
    }

    internal sealed record HistoryEntry(
        IEditorChange Change,
        EditorExecutionDescription Description,
        string? MergeKey,
        DateTimeOffset Timestamp);
}
