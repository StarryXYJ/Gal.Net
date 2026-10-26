using GalNet.Editor.Models.Graph;

namespace GalNet.Editor.Services;

public sealed partial class EntryEditSession
{
    internal sealed class DraftOwner(EntryEditorItemViewModel owner)
    {
        private readonly WeakReference<EntryEditorItemViewModel> _view = new(owner);
        private readonly Func<EntryEditorItemViewModel> _create = owner.DraftOwnerFactory();
        public EntryEditorItemViewModel Get() => _view.TryGetTarget(out var view) ? view : _create();
    }

    // A mounted field owns its clean state. Only unfinished input is strongly retained by the project.
    private sealed class DraftRepository
    {
        private readonly Dictionary<DraftKey, WeakReference<Draft>> _items = [];
        private readonly HashSet<Draft> _pending = [];
        public IEnumerable<Draft> Values => Live().Select(pair => pair.Value);
        public IEnumerable<KeyValuePair<DraftKey, Draft>> Where(Func<KeyValuePair<DraftKey, Draft>, bool> predicate) => Live().Where(predicate);
        public bool TryGetValue(DraftKey key, out Draft value)
        {
            if (_items.TryGetValue(key, out var reference) && reference.TryGetTarget(out value!)) return true;
            value = null!; return false;
        }
        public void Add(DraftKey key, Draft value)
        {
            Prune(); _items.Add(key, new(value)); Track(value);
        }
        public Draft this[DraftKey key] { set { _items[key] = new(value); Track(value); } }
        public void Track(Draft draft)
        { if (draft.State.IsDirty || !draft.State.CanCommit) _pending.Add(draft); else _pending.Remove(draft); }
        public void Remove(DraftKey key)
        { if (TryGetValue(key, out var value)) _pending.Remove(value); _items.Remove(key); }
        public void Clear() { _items.Clear(); _pending.Clear(); }
        private IEnumerable<KeyValuePair<DraftKey, Draft>> Live()
        {
            _pending.RemoveWhere(draft => !draft.State.IsDirty && draft.State.CanCommit);
            foreach (var pair in _items.ToArray()) if (pair.Value.TryGetTarget(out var draft)) yield return new(pair.Key, draft);
        }
        private void Prune()
        {
            _pending.RemoveWhere(draft => !draft.State.IsDirty && draft.State.CanCommit);
            foreach (var key in _items.Where(pair => !pair.Value.TryGetTarget(out _)).Select(pair => pair.Key).ToArray()) _items.Remove(key);
        }
    }
}
