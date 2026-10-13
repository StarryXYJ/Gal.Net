namespace GalNet.Runtime.Progress;

/// <summary>Per-player progress that must not be restored by a save slot.</summary>
public interface IGameProgressService
{
    bool IsRead(string groupId, string entryId);
    void MarkRead(string groupId, string entryId);
    /// <summary>Commits pending progress at a save or shutdown boundary. In-memory hosts need no I/O.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
