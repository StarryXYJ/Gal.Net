using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.Shared.Services;

/// <summary>Project-owned input participants, independent of publication locks and saved baselines.</summary>
internal sealed class ProjectDraftRegistry
{
    private readonly List<Registration> _items = [];
    public bool HasChanges => _items.Any(item => item.HasChanges());
    public Registration[] Pending => _items.Where(item => item.HasChanges()).ToArray();

    public IDisposable Register(Func<bool> hasChanges, Func<Task> commit, Action? discard = null,
        string? fileId = null, Action? validate = null, Func<ProjectDraftCommitContext, Task>? contextualCommit = null)
    {
        var registration = new Registration(_items, hasChanges, commit, fileId, validate, discard, contextualCommit);
        _items.Add(registration);
        return registration;
    }
    public async Task CommitAsync(ProjectDraftCommitContext context = default)
    {
        foreach (var item in _items.ToArray())
            if (item.HasChanges()) await (item.ContextualCommit?.Invoke(context) ?? item.Commit());
    }
    public async Task CommitFileAsync(string fileId)
    {
        var pending = _items.Where(item => item.FileId == fileId && item.HasChanges()).ToArray();
        foreach (var item in pending) item.Validate?.Invoke();
        foreach (var item in pending) await item.Commit();
    }
    public void Clear() => _items.Clear();
    internal sealed record Registration(List<Registration> Owner, Func<bool> HasChanges, Func<Task> Commit,
        string? FileId, Action? Validate, Action? Discard, Func<ProjectDraftCommitContext, Task>? ContextualCommit) : IDisposable
    {
        public void Dispose() => Owner.Remove(this);
    }
}
