using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Project;

namespace GalNet.Editor.Abstraction.Services;

public interface IProjectContentSession
{
    bool IsDirty { get; }
    bool RepairRequired { get; }
    Exception? LastError { get; }
    Exception? LastRecoveryError { get; }
    event Action? Changed;
    IDisposable RegisterDraft(Func<bool> hasChanges, Func<Task> commit);
    IDisposable RegisterCaptureGuard(Func<bool, CancellationToken, Task<IDisposable>> acquire);
    Task CommitDraftsAsync();
    void Open(GalProject project);
    void Refresh();
    void BlockWrites(Exception failure);
    void ReportError(Exception failure);
    Task SaveAsync(CancellationToken cancellationToken = default);
    Task SaveViewStateAsync();
    Task PublishStructureAsync(IProjectStructureChange change);
    EditorProjectDocument CaptureWorking();
    EditorProjectDocument CaptureSaved();
    Task SuspendAsync();
    void ResumeSaves();
}
