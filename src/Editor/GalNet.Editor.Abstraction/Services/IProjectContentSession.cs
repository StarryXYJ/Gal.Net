using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Project;

namespace GalNet.Editor.Abstraction.Services;

public interface IProjectContentSession
{
    bool IsDirty { get; }
    bool IsPublishing => false;
    bool HasEditsSincePublication => false;
    bool RepairRequired { get; }
    Exception? LastError { get; }
    Exception? LastRecoveryError { get; }
    event Action? Changed;
    IDisposable RegisterDraft(Func<bool> hasChanges, Func<Task> commit);
    IDisposable RegisterFileDraft(string fileId, Func<bool> hasChanges, Func<Task> commit, Action validate) => RegisterDraft(hasChanges, commit);
    Task CommitFileDraftsAsync(string fileId) => CommitDraftsAsync();
    IDisposable RegisterCaptureGuard(Func<bool, CancellationToken, Task<IDisposable>> acquire);
    Task CommitDraftsAsync();
    void Open(GalProject project);
    void Refresh();
    void BlockWrites(Exception failure);
    void ReportError(Exception failure);
    Task SaveAsync(CancellationToken cancellationToken = default);
    Task SaveViewStateAsync();
    Task PublishStructureAsync(IProjectStructureChange change);
    Task PublishStructureAsync(Func<EditorProjectDocument, EditorProjectDocument, IProjectStructureChange> prepare);
    EditorProjectDocument CaptureWorking();
    EditorProjectDocument CaptureSaved();
    IReadOnlyDictionary<string, EditorFileDocument> FileDocuments => new Dictionary<string, EditorFileDocument>();
    EditorFileDocument TrackFile(string path, byte[]? textBytes = null) => throw new NotSupportedException();
    EditorFileDocument TrackLocalizationFile(string path, byte[] textBytes) => throw new NotSupportedException();
    Task SuspendAsync();
    void ResumeSaves();
}
