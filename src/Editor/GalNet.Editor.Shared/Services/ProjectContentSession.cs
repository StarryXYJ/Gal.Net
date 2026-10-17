using System.Text.Json;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Commands;

namespace GalNet.Editor.Shared.Services;

/// <summary>One captured author version and one publication plan for every GUI save.</summary>
public sealed class ProjectContentSession(IEditorDocumentService documents, IEditorDocumentRepository repository, ProjectSessionOptions? options = null) : IProjectContentSession, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private GalProject? _project;
    private byte[] _baseline = [];
    private byte[] _settingsBaseline = [];
    private bool _suspended;
    private bool _disposed;
    private readonly List<DraftRegistration> _drafts = [];
    private Func<bool, CancellationToken, Task<IDisposable>>? _captureGuard;
    private readonly CancellationTokenSource _lifetime = new();
    private ProjectRecoveryStore? _recovery;
    private IDisposable? _closingRegistration;
    private Task? _recoveryLoop;
    private readonly object _recoverySync = new();
    public Exception? LastRecoveryError { get; private set; }
    public Exception? LastNotificationError { get; private set; }

    public bool IsPublishing => !_disposed && _saveGate.CurrentCount == 0;
    public bool IsDirty => _project is not null && (_drafts.Any(draft => draft.HasChanges()) || !Serialize(Capture()).AsSpan().SequenceEqual(_baseline));
    public bool RepairRequired { get; private set; }
    public Exception? LastError { get; private set; }
    public event Action? Changed;

    public IDisposable RegisterDraft(Func<bool> hasChanges, Func<Task> commit)
    {
        var registration = new DraftRegistration(_drafts, hasChanges, commit);
        _drafts.Add(registration);
        return registration;
    }

    public IDisposable RegisterCaptureGuard(Func<bool, CancellationToken, Task<IDisposable>> acquire)
    {
        if (_captureGuard is not null) throw new InvalidOperationException("A project already has a content history.");
        _captureGuard = acquire;
        return new CaptureRegistration(this);
    }

    private sealed class CaptureRegistration(ProjectContentSession owner) : IDisposable
    {
        public void Dispose() => owner._captureGuard = null;
    }

    public async Task CommitDraftsAsync()
    {
        foreach (var draft in _drafts.ToArray())
            if (draft.HasChanges()) await draft.Commit();
    }

    private sealed record DraftRegistration(List<DraftRegistration> Owner, Func<bool> HasChanges, Func<Task> Commit) : IDisposable
    {
        public void Dispose() => Owner.Remove(this);
    }

    public void Open(GalProject project)
    {
        if (_project is not null) throw new InvalidOperationException("The content session is already open.");
        ProjectFileOperation.CleanupOrphans(project.RootPath);
        _project = project;
        _baseline = Serialize(Capture());
        _settingsBaseline = File.ReadAllBytes(Path.Combine(project.RootPath, "settings.json"));
        _recovery = new(project.RootPath, options ?? new());
        _closingRegistration = project.RegisterClosingCallback(async () =>
        {
            await SuspendAsync();
            _lifetime.Cancel();
            if (_recoveryLoop is not null) await _recoveryLoop;
            if (IsDirty) CaptureRecovery();
        });
        _recoveryLoop = RunRecoveryAsync();
    }

    public void Refresh()
    {
        if (_project is null) return;
        _project.Name = documents.CurrentDocument.Name;
        _project.IsDirty = IsDirty;
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        if (Changed is null) return;
        foreach (Action observer in Changed.GetInvocationList())
            try { observer(); }
            catch (Exception failure) { LastNotificationError = failure; Serilog.Log.Error(failure, "A project content observer failed"); }
    }

    public void BlockWrites(Exception failure)
    {
        if (RepairRequired) return;
        RepairRequired = true;
        LastError = failure;
        CaptureRecovery();
        Refresh();
    }

    public void ReportError(Exception failure)
    {
        LastError = failure;
        CaptureRecovery();
        Refresh();
    }

    private EditorProjectDocument Capture()
    {
        var project = _project ?? throw new InvalidOperationException("No project is open.");
        return EditorDocumentCloner.Clone(new()
        {
            Graph = documents.CurrentDocument,
            Settings = project.Settings,
            GroupEntries = documents.LoadedProject.GroupEntries
        });
    }

    public EditorProjectDocument CaptureWorking() => Capture();
    public EditorProjectDocument CaptureSaved()
    {
        var saved = JsonSerializer.Deserialize<EditorProjectDocument>(_baseline, JsonOptions)
            ?? throw new InvalidOperationException("No saved author baseline is available.");
        foreach (var entries in saved.GroupEntries.Values)
            for (var index = 0; index < entries.Count; index++) entries[index].Id = index + 1;
        return saved;
    }

    private static byte[] Serialize(EditorProjectDocument document)
    {
        // Lookup insertion order and displayed line numbers are not persisted author content.
        document.GroupEntries = document.GroupEntries.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var entry in document.GroupEntries.Values.SelectMany(entries => entries))
        {
            entry.Id = 0;
            entry.Parameters = entry.Parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value);
        }
        // Runtime's settings variable copies are derived, never a second author truth.
        document.Settings.PlayerVariables = document.Graph.PlayerVariables.Select(item => item.Clone()).ToList();
        document.Settings.SaveVariables = document.Graph.SaveVariables.Select(item => item.Clone()).ToList();
        return JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_suspended) throw new InvalidOperationException("Project publication is suspended while closing.");
        try { await CommitDraftsAsync(); }
        catch (Exception failure) { ReportError(failure); throw; }
        var captureLease = await AcquirePlanAsync(false, cancellationToken);
        try
        {
            if (_suspended) throw new InvalidOperationException("Project publication is suspended while closing.");
            if (RepairRequired) throw new InvalidOperationException("The project requires repair before further writes.");
            var project = _project ?? throw new InvalidOperationException("No project is open.");
            ProjectSavePlan plan;
            byte[] capturedContent;
            byte[] settings;
            var settingsPath = Path.Combine(project.RootPath, "settings.json");
            try
            {
                var snapshot = Capture();
                capturedContent = Serialize(snapshot);
                if (!File.ReadAllBytes(settingsPath).AsSpan().SequenceEqual(_settingsBaseline))
                    throw new EditorOperationException("project.externalSettings", "Project settings changed outside the editor. Reload before saving.");
                var content = repository.PrepareSave(project.RootPath, snapshot.Graph,
                    snapshot.GroupEntries.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<EditorEntryData>)pair.Value));
                settings = JsonSerializer.SerializeToUtf8Bytes(snapshot.Settings, JsonOptions);
                var writes = new Dictionary<string, byte[]>(content.Writes)
                {
                    ["settings.json"] = settings,
                    [".galnet/editor-state.json"] = JsonSerializer.SerializeToUtf8Bytes(project.EditorState, JsonOptions)
                };
                plan = new ProjectSavePlan(writes, content.DeletedFiles);
            }
            finally { captureLease?.Dispose(); captureLease = null; }
            // Capture and plan on the UI caller; publish detached bytes while newer author edits remain possible.
            await Task.Run(() =>
            {
                if (!File.ReadAllBytes(settingsPath).AsSpan().SequenceEqual(_settingsBaseline))
                    throw new EditorOperationException("project.externalSettings", "Project settings changed before publication.");
                repository.ApplySave(project.RootPath, plan, cancellationToken);
            }, cancellationToken);
            _settingsBaseline = settings;
            _baseline = capturedContent;
            LastError = null;
            Refresh();
        }
        catch (Exception error)
        {
            LastError = error;
            if (error is ProjectRepairRequiredException) RepairRequired = true;
            CaptureRecovery();
            Refresh();
            throw;
        }
        finally { captureLease?.Dispose(); _saveGate.Release(); }
    }

    public async Task SuspendAsync()
    {
        _suspended = true;
        await _saveGate.WaitAsync();
        _saveGate.Release();
    }

    public void ResumeSaves() => _suspended = false;

    private void CaptureRecovery()
    {
        lock (_recoverySync)
        {
            if (_disposed) return;
            try { _recovery?.Capture(Capture()); LastRecoveryError = null; }
            catch (Exception error) { LastRecoveryError = error; }
        }
    }

    private async Task RunRecoveryAsync()
    {
        using var timer = new PeriodicTimer((options ?? new()).RecoveryInterval);
        var cancellationToken = _lifetime.Token;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (_disposed || _suspended) continue;
                using var lease = _captureGuard is null ? null : await _captureGuard(false, cancellationToken);
                if (!_disposed && !_suspended && IsDirty) { CaptureRecovery(); NotifyChanged(); }
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task SaveViewStateAsync()
    {
        if (_disposed || _suspended) return;
        await _saveGate.WaitAsync();
        try
        {
            if (_disposed || _suspended || RepairRequired || _project is null) return;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(_project.EditorState, JsonOptions);
            repository.ApplySave(_project.RootPath, new(new Dictionary<string, byte[]> { [".galnet/editor-state.json"] = bytes }, []));
        }
        finally { _saveGate.Release(); }
    }

    /// <summary>Small author-file structure plans (Graph/Group/settings), shared by later Graph and file edits.
    /// Publication and the final in-memory transform run without yielding, so unrelated edits cannot slip between them.</summary>
    public Task PublishStructureAsync(IProjectStructureChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return PublishStructureAsync((_, _) => change);
    }

    public async Task PublishStructureAsync(Func<EditorProjectDocument, EditorProjectDocument, IProjectStructureChange> prepare)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var lease = await AcquirePlanAsync(true, CancellationToken.None);
        try
        {
            if (_suspended || RepairRequired) throw new InvalidOperationException("Project writes are unavailable.");
            var project = _project ?? throw new InvalidOperationException("No project is open.");
            var working = Capture();
            var saved = JsonSerializer.Deserialize<EditorProjectDocument>(_baseline, JsonOptions)!;
            var change = prepare(working, saved);
            var nextWorking = EditorDocumentCloner.Clone(working);
            var nextSaved = EditorDocumentCloner.Clone(saved);
            change.Apply(nextWorking, AuthorDocumentVersion.Working);
            change.Apply(nextSaved, AuthorDocumentVersion.Saved);
            ValidateWorkingStructure(project.RootPath, nextWorking);
            _ = Serialize(nextWorking);
            var plan = PrepareAuthorPlan(project, nextSaved);
            if (change is IProjectFileStructureChange files) plan = plan with { Files = files.Files };
            repository.ApplySave(project.RootPath, plan);
            try
            {
                change.Apply(new() { Graph = documents.CurrentDocument, Settings = project.Settings, GroupEntries = documents.LoadedProject.GroupEntries }, AuthorDocumentVersion.Working);
                documents.SynchronizeGroups();
            }
            catch (Exception failure)
            {
                try
                {
                    var restore = PrepareAuthorPlan(project, saved, checkSettingsBaseline: false);
                    repository.ApplySave(project.RootPath, restore);
                    _settingsBaseline = restore.Writes["settings.json"];
                }
                catch (Exception compensation)
                {
                    BlockWrites(new AggregateException("Structure publication could not be compensated.", failure, compensation));
                    throw new ProjectRepairRequiredException(Path.Combine(project.RootPath, ".galnet/editor"), LastError!);
                }
                throw;
            }
            _baseline = Serialize(nextSaved);
            _settingsBaseline = plan.Writes["settings.json"];
            LastError = null;
            Refresh();
        }
        catch (Exception failure)
        {
            LastError = failure;
            if (failure is ProjectRepairRequiredException) RepairRequired = true;
            CaptureRecovery();
            Refresh();
            throw;
        }
        finally { _saveGate.Release(); }
    }

    private ProjectSavePlan PrepareAuthorPlan(GalProject project, EditorProjectDocument snapshot, bool checkSettingsBaseline = true)
    {
        if (checkSettingsBaseline && !File.ReadAllBytes(Path.Combine(project.RootPath, "settings.json")).AsSpan().SequenceEqual(_settingsBaseline))
            throw new EditorOperationException("project.externalSettings", "Project settings changed outside the editor.");
        _ = Serialize(snapshot);
        var content = repository.PrepareSave(project.RootPath, snapshot.Graph,
            snapshot.GroupEntries.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<EditorEntryData>)pair.Value));
        return new(new Dictionary<string, byte[]>(content.Writes) { ["settings.json"] = JsonSerializer.SerializeToUtf8Bytes(snapshot.Settings, JsonOptions) }, content.DeletedFiles);
    }

    private static void ValidateWorkingStructure(string projectPath, EditorProjectDocument document)
    {
        var reader = new ProjectRawReader(projectPath);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var node in document.Graph.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id) || !ids.Add(node.Id)) throw new InvalidDataException("Working node IDs must be non-empty and unique.");
            if (node.Type != "Group") continue;
            if (string.IsNullOrWhiteSpace(node.File) || Path.IsPathRooted(node.File)) throw new InvalidDataException("Working Group source must be relative to Graph/.");
            var path = reader.Relative(reader.Resolve(Path.Combine("Graph", node.File)));
            if (!path.StartsWith("Graph/", StringComparison.Ordinal) || !path.EndsWith(".rawgalgroup", StringComparison.OrdinalIgnoreCase) || !paths.Add(path))
                throw new InvalidDataException("Working Group paths must be unique Raw sources within Graph/.");
            if (!document.GroupEntries.ContainsKey(node.Id)) throw new InvalidDataException("A working Group has no author document.");
        }
    }

    private async Task<IDisposable?> AcquirePlanAsync(bool structure, CancellationToken cancellationToken)
    {
        // Never hold the publication gate while waiting for history: a running structural edit may need to publish.
        while (true)
        {
            var lease = _captureGuard is null ? null : await _captureGuard(structure, cancellationToken);
            try { if (_saveGate.Wait(0, cancellationToken)) return lease; }
            catch { lease?.Dispose(); throw; }
            lease?.Dispose();
            await _saveGate.WaitAsync(cancellationToken);
            _saveGate.Release();
        }
    }

    public void Dispose()
    {
        lock (_recoverySync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _lifetime.Cancel();
        _closingRegistration?.Dispose();
        _drafts.Clear();
        _lifetime.Dispose();
        _saveGate.Dispose();
        GC.SuppressFinalize(this);
    }
}
