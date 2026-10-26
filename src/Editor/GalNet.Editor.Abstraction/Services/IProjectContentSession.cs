using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Project;

namespace GalNet.Editor.Abstraction.Services;

public readonly record struct ProjectDraftCommitContext(IReadOnlySet<string>? RemovingGroups = null);

public interface IProjectContentSession
{
    bool IsDirty { get; }
    bool IsPublishing { get; }
    bool HasEditsSincePublication { get; }
    bool RepairRequired { get; }
    Exception? LastError { get; }
    Exception? LastRecoveryError { get; }
    event Action? Changed;
    event Action? ExternalReloaded;
    IDisposable RegisterDraft(Func<bool> hasChanges, Func<Task> commit);
    IDisposable RegisterDraft(Func<bool> hasChanges, Func<Task> commit, Action discard);
    IDisposable RegisterDraft(Func<bool> hasChanges, Func<ProjectDraftCommitContext, Task> commit, Action discard);
    /// <summary>Wraps the synchronous publication boundary after preparation/validation and before any file or live author mutation.</summary>
    IDisposable RegisterStructureTransition(Action<EditorProjectDocument, EditorProjectDocument, Action> transition);
    IDisposable RegisterFileDraft(string fileId, Func<bool> hasChanges, Func<Task> commit, Action validate);
    IDisposable RegisterFileDraft(string fileId, Func<bool> hasChanges, Func<Task> commit, Action validate, Action discard);
    Task CommitFileDraftsAsync(string fileId);
    IDisposable RegisterCaptureGuard(Func<bool, CancellationToken, Task<IDisposable>> acquire);
    Task CommitDraftsAsync();
    Task CommitDraftsAsync(ProjectDraftCommitContext context);
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
    Task<ExternalReloadResult> ReloadExternalAsync(ExternalProjectSnapshot snapshot, bool discardLocalChanges = false, CancellationToken cancellationToken = default);
    IReadOnlyDictionary<string, EditorFileDocument> FileDocuments { get; }
    EditorFileDocument TrackFile(string path, byte[]? textBytes = null);
    EditorFileDocument TrackLocalizationFile(string path, byte[] textBytes);
    Task SuspendAsync();
    void ResumeSaves();
}
