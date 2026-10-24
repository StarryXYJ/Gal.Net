using GalNet.Core.Parameters;

namespace GalNet.Game.Controls.Forms;

/// <summary>Optional host-owned input state, independent of a mounted editor's lifetime.</summary>
public sealed class FieldDraftState(Func<string> readSource)
{
    private readonly List<Func<bool>> _guards = [];
    private bool _conflicted;
    public bool CanCommit => _guards.All(guard => guard());
    public IDisposable RegisterGuard(Func<bool> canCommit)
    {
        _guards.Add(canCommit); return new Guard(() => _guards.Remove(canCommit));
    }
    private sealed class Guard(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
    public string Text { get; private set; } = "";
    public bool IsDirty { get; private set; }
    public bool JsonText { get; private set; }
    public ValueIssue? Issue { get; private set; }
    public string? Baseline { get; private set; }
    public bool IsConflicted => IsDirty && (_conflicted || Baseline != readSource());
    // A later undo can restore equal JSON without restoring the original editing identity.
    public void CheckSource() { if (IsConflicted) _conflicted = true; }
    public Func<Task<ValueIssue?>>? Commit { get; set; }
    public Func<FieldDescriptor, string, FieldDraftState>? Child { get; set; }
    public event Action? Changed;

    public void Update(string text, bool dirty, ValueIssue? issue, bool jsonText)
    {
        if (Text == text && IsDirty == dirty && Equals(Issue, issue) && JsonText == jsonText) return;
        if (dirty && !IsDirty) Baseline = readSource();
        Text = text; IsDirty = dirty; Issue = issue; JsonText = jsonText;
        if (!dirty) { Baseline = null; _conflicted = false; }
        Changed?.Invoke();
    }

    public void Clear() => Update("", false, null, false);
    public void Refresh() => Changed?.Invoke();
}
