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
    private readonly ProjectContentBaseline _savedContent = new();
    private byte[] BaselineBytes { get => _savedContent.Bytes; set => _savedContent.Set(value); }
    private byte[] _settingsBaseline = [];
    private Dictionary<string, byte[]> _externalBaseline = new(ProjectFilePaths.Comparer);
    private string _contractsFingerprint = "";
    private ProjectContentBaseline? _publicationContent;
    private bool _suspended;
    private bool _disposed;
    private readonly ProjectDraftRegistry _drafts = new();
    private readonly List<Action<EditorProjectDocument, EditorProjectDocument, Action>> _structureTransitions = [];
    private readonly Dictionary<string, EditorFileDocument> _files = [];
    public IReadOnlyDictionary<string, EditorFileDocument> FileDocuments => _files;
    public EditorFileDocument TrackFile(string path, byte[]? textBytes = null)
    {
        path = textBytes is null ? ProjectFilePaths.Normalize(path) : ProjectFilePaths.Editable(path);
        _ = new ProjectRawReader(_project!.RootPath).Resolve(path);
        var existing = _files.Values.FirstOrDefault(file => file.Exists && ProjectFilePaths.Comparer.Equals(file.Path, path));
        if (existing is not null) return existing;
        if (IsPublishing) throw ProjectFilePaths.Error("busy", path);
        var file = new EditorFileDocument { Path = path, ManagedContent = textBytes is not null, Data = textBytes?.ToArray() ?? [] };
        _files.Add(file.Id, file);
        var saved = JsonSerializer.Deserialize<EditorProjectDocument>(BaselineBytes, JsonOptions)!;
        saved.Files.Add(file.Id, EditorDocumentCloner.Clone(new EditorProjectDocument { Files = new() { [file.Id] = file } }).Files[file.Id]);
        BaselineBytes = Serialize(saved);
        if (textBytes is not null) _externalBaseline[path] = textBytes.ToArray();
        Refresh();
        return file;
    }
    public EditorFileDocument TrackLocalizationFile(string path, byte[] textBytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        path = LocalizationJson.Path(path);
        if (_suspended || RepairRequired || IsPublishing) throw ProjectFilePaths.Error("busy", path);
        var full = new ProjectRawReader(_project!.RootPath).Resolve(path);
        if (!File.Exists(full) || !File.ReadAllBytes(full).AsSpan().SequenceEqual(textBytes)) throw ProjectFilePaths.Error("changed", path);
        var existing = _files.Values.SingleOrDefault(file => file.Exists && ProjectFilePaths.Comparer.Equals(file.Path, path));
        if (existing is { ManagedContent: true }) return existing;
        var saved = CaptureSaved();
        var file = existing ?? new EditorFileDocument { Path = path };
        file.ManagedContent = true; file.Data = textBytes.ToArray();
        _files[file.Id] = file;
        saved.Files[file.Id] = new() { Id = file.Id, Path = path, ManagedContent = true, Data = textBytes.ToArray() };
        BaselineBytes = Serialize(saved);
        _externalBaseline[path] = textBytes.ToArray();
        Refresh();
        return file;
    }
    private Func<bool, CancellationToken, Task<IDisposable>>? _captureGuard;
    private readonly CancellationTokenSource _lifetime = new();
    private ProjectRecoveryStore? _recovery;
    private IDisposable? _closingRegistration;
    private Task? _recoveryLoop;
    private readonly object _recoverySync = new();
    public Exception? LastRecoveryError { get; private set; }
    public Exception? LastNotificationError { get; private set; }

    public bool IsPublishing => !_disposed && _saveGate.CurrentCount == 0;
    public bool HasEditsSincePublication => IsPublishing && _publicationContent is { } captured &&
        (_drafts.HasChanges || !captured.Matches(CurrentContent()));
    public bool IsDirty => _project is not null && (_drafts.HasChanges || !_savedContent.Matches(CurrentContent()));
    public bool RepairRequired { get; private set; }
    public Exception? LastError { get; private set; }
    public event Action? Changed;
    public event Action? ExternalReloaded;

    public IDisposable RegisterDraft(Func<bool> hasChanges, Func<Task> commit) => _drafts.Register(hasChanges, commit);
    public IDisposable RegisterDraft(Func<bool> hasChanges, Func<Task> commit, Action discard) => _drafts.Register(hasChanges, commit, discard);
    public IDisposable RegisterDraft(Func<bool> hasChanges, Func<ProjectDraftCommitContext, Task> commit, Action discard) =>
        _drafts.Register(hasChanges, () => commit(default), discard, contextualCommit: commit);
    public IDisposable RegisterStructureTransition(Action<EditorProjectDocument, EditorProjectDocument, Action> transition)
    {
        _structureTransitions.Add(transition); return new StructureRegistration(_structureTransitions, transition);
    }
    private sealed record StructureRegistration(List<Action<EditorProjectDocument, EditorProjectDocument, Action>> Owner,
        Action<EditorProjectDocument, EditorProjectDocument, Action> Transition) : IDisposable
    { public void Dispose() => Owner.Remove(Transition); }
    public IDisposable RegisterFileDraft(string fileId, Func<bool> hasChanges, Func<Task> commit, Action validate) =>
        _drafts.Register(hasChanges, commit, fileId: fileId, validate: validate);
    public IDisposable RegisterFileDraft(string fileId, Func<bool> hasChanges, Func<Task> commit, Action validate, Action discard) =>
        _drafts.Register(hasChanges, commit, discard, fileId, validate);
    public Task CommitFileDraftsAsync(string fileId) => _drafts.CommitFileAsync(fileId);

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

    public Task CommitDraftsAsync() => _drafts.CommitAsync();
    public Task CommitDraftsAsync(ProjectDraftCommitContext context) => _drafts.CommitAsync(context);

    public void Open(GalProject project)
    {
        if (_project is not null) throw new InvalidOperationException("The content session is already open.");
        ProjectFileOperation.CleanupOrphans(project.RootPath);
        _project = project;
        _externalBaseline = ExternalProjectSnapshotReader.ReadFiles(project.RootPath, []);
        foreach (var pair in repository.SourceBaseline)
            if (pair.Value is { } bytes) _externalBaseline[pair.Key] = bytes.ToArray();
        foreach (var (path, bytes) in _externalBaseline.Where(pair => ExternalProjectSnapshotReader.IsLocalization(pair.Key)))
        {
            var file = new EditorFileDocument { Path = path, ManagedContent = true, Data = bytes.ToArray() };
            _files.Add(file.Id, file);
        }
        _contractsFingerprint = ExternalProjectSnapshotReader.ContractsFingerprint(project.RootPath);
        BaselineBytes = Serialize(Capture());
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

    private EditorProjectDocument CurrentContent()
    {
        var project = _project ?? throw new InvalidOperationException("No project is open.");
        return new()
        {
            Graph = documents.CurrentDocument,
            Configuration = project.Configuration,
            GroupEntries = documents.LoadedProject.GroupEntries,
            Files = _files
        };
    }

    private EditorProjectDocument Capture() => EditorDocumentCloner.Clone(CurrentContent());

    public EditorProjectDocument CaptureWorking() => Capture();

    public async Task<ExternalReloadResult> ReloadExternalAsync(ExternalProjectSnapshot snapshot, bool discardLocalChanges = false, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_project is null || _suspended || RepairRequired) return ExternalReloadResult.Unavailable;
        using var lease = await AcquirePlanAsync(false, cancellationToken);
        try
        {
            if (_disposed || _suspended || RepairRequired) return ExternalReloadResult.Unavailable;
            if (snapshot.ContractsFingerprint != _contractsFingerprint)
                throw new EditorOperationException("project.externalContracts", "Project contracts changed. Reopen the project to load the new contract safely.");
            if (!ExternalProjectSnapshotReader.IsCurrent(_project.RootPath, snapshot, _files.Values)) return ExternalReloadResult.Retry;
            if (ExternalProjectSnapshotReader.Same(snapshot.Files, _externalBaseline)) return ExternalReloadResult.Unchanged;
            _project.Configurations.Validate(snapshot.Configuration);
            if (IsDirty && !discardLocalChanges) return ExternalReloadResult.LocalChanges;
            if (discardLocalChanges)
            {
                var pending = _drafts.Pending;
                if (pending.Any(draft => draft.Discard is null)) return ExternalReloadResult.LocalChanges;
                CaptureRecovery();
                foreach (var draft in pending) draft.Discard!();
                // Composition/in-flight input may refuse cancellation. Keep the live version until it settles.
                if (_drafts.HasChanges) return ExternalReloadResult.LocalChanges;
            }
            // All fallible disk inspection precedes the in-memory version transition.
            repository.AdoptExternalBaseline(snapshot.AuthorSources);
            _project.Configuration.ReplaceFrom(snapshot.Configuration);
            foreach (var group in _files.Values.Where(file => file.ManagedContent).GroupBy(file => file.Path, ProjectFilePaths.Comparer))
            {
                var file = group.FirstOrDefault(file => file.Exists) ?? group.First();
                foreach (var item in group) item.Exists = false;
                file.Exists = snapshot.Files.TryGetValue(file.Path, out var bytes);
                file.Data = bytes?.ToArray() ?? [];
            }
            foreach (var (path, bytes) in snapshot.Files.Where(pair => ExternalProjectSnapshotReader.IsLocalization(pair.Key)))
                if (!_files.Values.Any(file => file.Exists && ProjectFilePaths.Comparer.Equals(file.Path, path)))
                {
                    var file = new EditorFileDocument { Path = path, ManagedContent = true, Data = bytes.ToArray() };
                    _files.Add(file.Id, file);
                }
            documents.Reload(snapshot.Document);
            _settingsBaseline = snapshot.Files["settings.json"].ToArray();
            _externalBaseline = snapshot.Files.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), ProjectFilePaths.Comparer);
            BaselineBytes = Serialize(Capture());
            LastError = null;
            if (ExternalReloaded is not null)
                foreach (Action observer in ExternalReloaded.GetInvocationList())
                    try { observer(); }
                    catch (Exception failure) { LastNotificationError = failure; Serilog.Log.Error(failure, "An external reload observer failed"); }
            Refresh();
            return ExternalReloadResult.Reloaded;
        }
        finally { _saveGate.Release(); Refresh(); }
    }

    private void AcceptPublication(ProjectSavePlan plan)
    {
        foreach (var pair in plan.Writes)
            if (pair.Key == "settings.json" || pair.Key.StartsWith("Graph/", StringComparison.Ordinal) || _externalBaseline.ContainsKey(pair.Key) || ExternalProjectSnapshotReader.IsLocalization(pair.Key))
                _externalBaseline[pair.Key] = pair.Value.ToArray();
        foreach (var path in plan.DeletedFiles) _externalBaseline.Remove(path);
        if (plan.Files is { } files)
        {
            foreach (var path in files.Writes.Keys.Concat(files.Copies.Keys).Where(path =>
                path.StartsWith("Graph/", StringComparison.Ordinal) || path.StartsWith("I18n/", StringComparison.Ordinal) || _files.Values.Any(file => file.ManagedContent && file.Path == path)))
                _externalBaseline[path] = File.ReadAllBytes(new ProjectRawReader(_project!.RootPath).Resolve(path));
            foreach (var path in files.Deletes) _externalBaseline.Remove(path);
        }
    }
    public EditorProjectDocument CaptureSaved()
    {
        var saved = JsonSerializer.Deserialize<EditorProjectDocument>(BaselineBytes, JsonOptions)
            ?? throw new InvalidOperationException("No saved author baseline is available.");
        foreach (var entries in saved.GroupEntries.Values)
            for (var index = 0; index < entries.Count; index++) entries[index].Id = index + 1;
        return saved;
    }

    private static byte[] Serialize(EditorProjectDocument document)
    {
        // Lookup insertion order and displayed line numbers are not persisted author content.
        document.GroupEntries = document.GroupEntries.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value);
        document.Files = document.Files.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var entry in document.GroupEntries.Values.SelectMany(entries => entries))
        {
            entry.Id = 0;
            entry.Parameters = entry.Parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value);
        }
        return JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_suspended) throw new InvalidOperationException("Project publication is suspended while closing.");
        try { await CommitDraftsAsync(); }
        catch (Exception failure) { ReportError(failure); throw; }
        var captureLease = await AcquirePlanAsync(false, cancellationToken);
        Refresh();
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
                CheckTrackedFiles();
                capturedContent = Serialize(snapshot);
                _publicationContent = new();
                _publicationContent.Set(capturedContent);
                if (!File.ReadAllBytes(settingsPath).AsSpan().SequenceEqual(_settingsBaseline))
                    throw new EditorOperationException("project.externalSettings", "Project settings changed outside the editor. Reload before saving.");
                var content = repository.PrepareSave(project.RootPath, snapshot.Graph,
                    snapshot.GroupEntries.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<EditorEntryData>)pair.Value));
                project.Configurations.Validate(snapshot.Configuration);
                settings = JsonSerializer.SerializeToUtf8Bytes(snapshot.Configuration, JsonOptions);
                var writes = new Dictionary<string, byte[]>(content.Writes)
                {
                    ["settings.json"] = settings,
                    [".galnet/editor-state.json"] = JsonSerializer.SerializeToUtf8Bytes(project.EditorState, JsonOptions)
                };
                foreach (var file in snapshot.Files.Values.Where(file => file.Exists && file.ManagedContent)) writes[file.Path] = file.Data;
                plan = new ProjectSavePlan(writes, content.DeletedFiles) { Files = TrackedExpectations() };
            }
            finally { captureLease?.Dispose(); captureLease = null; }
            // Capture and plan on the UI caller; publish detached bytes while newer author edits remain possible.
            await Task.Run(() =>
            {
                CheckTrackedFiles();
                if (!File.ReadAllBytes(settingsPath).AsSpan().SequenceEqual(_settingsBaseline))
                    throw new EditorOperationException("project.externalSettings", "Project settings changed before publication.");
                repository.ApplySave(project.RootPath, plan, cancellationToken);
            }, cancellationToken);
            _settingsBaseline = settings;
            BaselineBytes = capturedContent;
            AcceptPublication(plan);
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
        finally { captureLease?.Dispose(); _publicationContent = null; _saveGate.Release(); Refresh(); }
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
            var saved = JsonSerializer.Deserialize<EditorProjectDocument>(BaselineBytes, JsonOptions)!;
            var change = prepare(working, saved);
            var nextWorking = EditorDocumentCloner.Clone(working);
            var nextSaved = EditorDocumentCloner.Clone(saved);
            change.Apply(nextWorking, AuthorDocumentVersion.Working);
            change.Apply(nextSaved, AuthorDocumentVersion.Saved);
            ValidateWorkingStructure(project.RootPath, nextWorking);
            _ = Serialize(nextWorking);
            var plan = PrepareAuthorPlan(project, nextSaved);
            if (change is IProjectFileStructureChange files)
            {
                foreach (var pair in plan.Files!.ExpectedHashes) files.Files.ExpectedHashes[pair.Key] = pair.Value;
                plan = plan with { Files = files.Files };
            }
            void Publish()
            {
                repository.ApplySave(project.RootPath, plan);
                try
                {
                    change.Apply(new() { Graph = documents.CurrentDocument, Configuration = project.Configuration, GroupEntries = documents.LoadedProject.GroupEntries, Files = _files }, AuthorDocumentVersion.Working);
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
                BaselineBytes = Serialize(nextSaved);
                _settingsBaseline = plan.Writes["settings.json"];
                AcceptPublication(plan);
                LastError = null;
            }
            Action publish = Publish;
            foreach (var transition in _structureTransitions.ToArray().Reverse())
            { var next = publish; publish = () => transition(working, nextWorking, next); }
            publish();
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
        project.Configurations.Validate(snapshot.Configuration);
        if (checkSettingsBaseline) CheckTrackedFiles();
        if (checkSettingsBaseline && !File.ReadAllBytes(Path.Combine(project.RootPath, "settings.json")).AsSpan().SequenceEqual(_settingsBaseline))
            throw new EditorOperationException("project.externalSettings", "Project settings changed outside the editor.");
        _ = Serialize(snapshot);
        var content = repository.PrepareSave(project.RootPath, snapshot.Graph,
            snapshot.GroupEntries.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<EditorEntryData>)pair.Value));
        var writes = new Dictionary<string, byte[]>(content.Writes) { ["settings.json"] = JsonSerializer.SerializeToUtf8Bytes(snapshot.Configuration, JsonOptions) };
        foreach (var file in snapshot.Files.Values.Where(file => file.Exists && file.ManagedContent)) writes[file.Path] = file.Data;
        var previous = JsonSerializer.Deserialize<EditorProjectDocument>(BaselineBytes, JsonOptions)!;
        var removed = previous.Files.Values.Where(file => file.ManagedContent && file.Exists &&
            (!snapshot.Files.TryGetValue(file.Id, out var next) || !next.Exists || !ProjectFilePaths.Comparer.Equals(file.Path, next.Path))).Select(file => file.Path);
        return new(writes, content.DeletedFiles.Concat(removed).Distinct(ProjectFilePaths.Comparer).ToArray()) { Files = TrackedExpectations() };
    }

    private ProjectFilePlan TrackedExpectations()
    {
        var plan = new ProjectFilePlan();
        var saved = JsonSerializer.Deserialize<EditorProjectDocument>(BaselineBytes, JsonOptions)!;
        foreach (var file in TrackedFiles(saved))
            plan.ExpectedHashes[file.Path] = file.Exists ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file.Data)) : null;
        return plan;
    }

    private void CheckTrackedFiles()
    {
        var saved = JsonSerializer.Deserialize<EditorProjectDocument>(BaselineBytes, JsonOptions)!;
        var reader = new ProjectRawReader(_project!.RootPath);
        foreach (var file in TrackedFiles(saved))
        {
            var path = reader.Resolve(file.Path);
            if (file.Exists ? !File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(file.Data) : File.Exists(path))
                throw ProjectFilePaths.Error("changed", file.Path);
        }
    }

    private static IEnumerable<EditorFileDocument> TrackedFiles(EditorProjectDocument document) => document.Files.Values
        .GroupBy(file => file.Path, ProjectFilePaths.Comparer)
        .Select(group => group.SingleOrDefault(file => file.Exists) ?? group.First())
        .Where(file => file.ManagedContent);

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
        _structureTransitions.Clear();
        _lifetime.Dispose();
        _saveGate.Dispose();
        GC.SuppressFinalize(this);
    }
}
