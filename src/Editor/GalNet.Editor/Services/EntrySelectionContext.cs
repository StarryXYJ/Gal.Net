namespace GalNet.Editor.Services;

public sealed record ActiveEntrySelection(System.Guid Owner, string GroupId, string? EntryId);

/// <summary>Project-owned active item; focusing a tool does not change the author selection.</summary>
public sealed class EntrySelectionContext
{
    public ActiveEntrySelection? Current { get; private set; }
    public event System.Action? Changed;
    public void Select(System.Guid owner, string groupId, string? entryId)
    {
        var next = new ActiveEntrySelection(owner, groupId, entryId);
        if (Current == next) return;
        Current = next; Changed?.Invoke();
    }
    public void Clear(System.Guid owner)
    {
        if (Current?.Owner != owner) return;
        Current = null; Changed?.Invoke();
    }
}
