using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.ViewModels;

public sealed record AuthorProblem(string Severity, string Message, string Source, AuthorNavigationTarget? Target, Action? Discard = null)
{
    public string? Code { get; init; }
    public FieldDescriptor? Field { get; init; }
    public IReadOnlyList<string>? FieldPath { get; init; }
    public string? Details { get; init; }
    public bool HasDetails => !string.IsNullOrEmpty(Details);
    public bool CanNavigate { get; init; }
    public string? NavigationHint { get; init; }
    public SceneReferenceDiagnostic? SceneReference { get; init; }
    public Action? Restore { get; init; }
    public string? InputText { get; init; }
    public bool HasRestore => Restore is not null;
    public bool HasInputText => InputText is not null;
    public bool HasDiscard => Discard is not null;
    public bool IsError => Severity == "error";
    public bool IsWarning => Severity == "warning";
    public string SeverityKey => "Uiux.Severity." + Severity;
}

public sealed partial class ProblemsPanelViewModel : ObservableObject, IDisposable
{
    private readonly EntryEditSession _edits;
    private readonly IProjectContentSession _content;
    private readonly IProjectService _projects;
    private readonly IEntryCatalog _catalog;
    private readonly EditorHistories _histories;
    private readonly IEditorLocalizationService _text;
    private readonly AuthorNavigationService _navigation;
    private readonly EditorGroupProjectionStore _groups;
    private readonly ProjectLocalizationQuery _localization;
    private readonly ProjectSceneTargets? _scenes;
    private IReadOnlyList<SceneReferenceDiagnostic> _sceneDiagnostics = [];
    private readonly List<AuthorProblem> _external = [];
    private long _authorRevision;
    private readonly IEditorDocumentService? _documents;
    private bool _incomplete;
    private bool _disposed, _checking;
    private readonly object _analysisGate = new();
    private CancellationTokenSource? _checkCancellation;
    private long _checkGeneration;
    private string _fileStamp = "";
    public Task ValidationTask { get; private set; } = Task.CompletedTask;
    [ObservableProperty] private bool _onlyErrors;
    public IEnumerable<AuthorProblem> VisibleProblems => Problems.Where(problem => !OnlyErrors || problem.IsError).OrderBy(problem => problem.IsError ? 0 : problem.IsWarning ? 1 : 2);
    partial void OnOnlyErrorsChanged(bool value) => OnPropertyChanged(nameof(VisibleProblems));
    private List<AuthorProblem> _validation = [];
    private long _revision;
    private long? _validatedRevision;
    public ObservableCollection<AuthorProblem> Problems { get; } = [];
    public int Count => Problems.Count;
    public string Status => _text[_checking ? "Problem.Checking" : _validatedRevision is null ? "Uiux.Validation.Unchecked" : _validatedRevision != _revision ? "Uiux.Validation.Stale"
        : Problems.Any(problem => problem.IsError) ? "Uiux.Validation.Failed" : _incomplete || _sceneDiagnostics.Any(diagnostic => diagnostic.Code is "scene.reference.uncertain" or "scene.coverage.unknown" or "scene.coverage.structure" or "scene.analysis.limit") ? "Uiux.Validation.Incomplete" : "Uiux.Validation.Passed"];
    public ProblemsPanelViewModel(EntryEditSession edits, IProjectContentSession content, IProjectService projects, IEntryCatalog catalog,
        EditorHistories histories, IEditorLocalizationService text, AuthorNavigationService navigation, EditorGroupProjectionStore groups, ProjectLocalizationQuery localization, ProjectSceneTargets? scenes = null, IEditorDocumentService? documents = null)
    {
        _edits = edits; _content = content; _projects = projects; _catalog = catalog; _histories = histories; _text = text; _navigation = navigation;
        _groups = groups;
        _scenes = scenes;
        if (scenes is not null) scenes.Changed += SceneChanged;
        _localization = localization; _authorRevision = histories.Content.AuthorChanges.Revision; _documents = documents;
        content.ExternalReloaded += ExternalReloaded;
        content.Changed += FilesChanged;
        edits.Changed += Refresh; histories.Changed += ContentChanged; text.PropertyChanged += LocaleChanged;
        _sceneDiagnostics = scenes?.Diagnostics() ?? [];
        ScheduleValidation();
    }
    private void LocaleChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => Refresh();
    private void SceneChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(SceneChanged); return; }
        if (_disposed || _scenes is null) return;
        _sceneDiagnostics = _scenes.Diagnostics();
        ScheduleValidation();
    }
    private void ContentChanged()
    {
        var revision = _histories.Content.AuthorChanges.Revision;
        if (revision != _authorRevision)
        {
            _authorRevision = revision;
            if (_scenes is not null) _sceneDiagnostics = _scenes.Diagnostics();
        }
        ScheduleValidation();
    }
    private void ExternalReloaded() { if (_scenes is not null) _sceneDiagnostics = _scenes.Diagnostics(); ScheduleValidation(); }
    private void FilesChanged()
    {
        var stamp = string.Join("\n", _content.FileDocuments.Values.OrderBy(file => file.Id, StringComparer.Ordinal)
            .Select(file => $"{file.Id}:{file.Path}:{file.Exists}:{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(file.Data)}"));
        if (stamp == _fileStamp) return;
        _fileStamp = stamp; ScheduleValidation();
    }
    private void ScheduleValidation()
    {
        if (_disposed) return;
        _revision++; _checking = true;
        _checkCancellation?.Cancel(); _checkCancellation?.Dispose();
        var cancellation = _checkCancellation = new();
        var generation = ++_checkGeneration;
        ValidationTask = CheckLaterAsync(generation, cancellation.Token);
        Refresh();
    }
    private async Task CheckLaterAsync(long generation, CancellationToken token)
    {
        try
        {
            await Task.Delay(220, token);
            var input = await Dispatcher.UIThread.InvokeAsync(() =>
                _disposed || generation != _checkGeneration || _projects.Current is null ? null
                    : new ValidationInput(_content.CaptureWorking(), _projects.Current.RootPath, _revision, _localization.Get()));
            if (input is null) return;
            var results = await Task.Run(() => { lock (_analysisGate) { token.ThrowIfCancellationRequested(); return Analyze(input.Snapshot, input.Root, input.Revision); } }, token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_disposed && generation == _checkGeneration) CompleteValidation(results, input.Localization, input.Snapshot, input.Revision);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed || generation != _checkGeneration) return;
                _validation = [new("error", _text["Problem.CheckFailed"], "", null) { Details = error.Message }];
                _incomplete = true; _validatedRevision = _revision; _checking = false; Refresh();
            });
        }
    }
    private sealed record ValidationInput(EditorProjectDocument Snapshot, string Root, long Revision, LocalizationReferenceIndex? Localization);
    private bool IsPresent(AuthorNavigationTarget? target) => _documents is null || target?.NodeId is null
        || _documents.CurrentDocument.Nodes.Any(node => node.Id == target.NodeId) && (target.EntryId is null
            || _documents.Groups.TryGetValue(target.NodeId, out var group) && group.Entries.Any(entry => entry.StableId == target.EntryId));
    private void Refresh()
    {
        if (_disposed) return;
        Problems.Clear();
        foreach (var draft in _edits.Pending)
            Problems.Add(Present(new(draft.State.IsConflicted || draft.State.Issue is not null ? "error" : "info", draft.State.IsConflicted ? _text["Uiux.Draft.Conflict"] : draft.State.Issue?.Message ?? _text["Uiux.Draft.Pending"],
                Source(new(draft.Owner.AuthorGroupId, draft.Owner.StableId, draft.Root.Id)), new(draft.Owner.AuthorGroupId, draft.Owner.StableId, draft.Root.Id, Revision: _revision, FieldPath: draft.Path), draft.State.Clear)
            {
                Field = draft.Field,
                FieldPath = draft.Path,
                Code = draft.State.IsConflicted ? "field.conflict" : draft.State.Issue?.Code,
                Details = draft.State.Issue?.Message,
            }));
        foreach (var archive in _edits.Archives)
        {
            var target = new AuthorNavigationTarget(archive.Owner.GroupId, archive.Owner.EntryId, archive.RootId, FieldPath: archive.Path);
            var ready = _edits.CanRestore(archive);
            Problems.Add(Present(new("info", _text[_edits.IsRestoreRequested(archive) ? "Uiux.Draft.RestoreRequested" : ready ? "Uiux.Draft.Recoverable" : "Uiux.Draft.Archived"],
                archive.Type + " / " + archive.RootId + (archive.Path.Length == 0 ? "" : "/" + string.Join("/", archive.Path)), target, () => _edits.RemoveArchive(archive))
            {
                InputText = archive.Text,
                FieldPath = archive.Path,
                Restore = ready ? () => { _edits.RequestRestore(archive); _navigation.Navigate(target); }
                : null
            }));
        }
        if (_edits.ArchiveError is not null) Problems.Add(new("error", _text["Error.entry.archiveUnavailable"], ".galnet/editor/input-archive.json", null));
        if (_validatedRevision == _revision)
            foreach (var problem in _validation.Where(problem => IsPresent(problem.Target))) Problems.Add(Present(problem));
        foreach (var diagnostic in _sceneDiagnostics)
            Problems.Add(Present(new("warning", _text["Scene.Diagnostic." + diagnostic.Code], diagnostic.Owner.GroupId + diagnostic.JsonPointer,
                new(diagnostic.Owner.GroupId, diagnostic.Owner.EntryId, diagnostic.JsonPointer.Split('/').ElementAtOrDefault(2), Revision: _revision))
            { SceneReference = diagnostic, Details = diagnostic.Code + "\n" + diagnostic.JsonPointer }));
        foreach (var problem in _external) Problems.Add(Present(problem));
        var distinct = Problems.DistinctBy(problem => (problem.Severity, problem.Message, problem.Source, problem.Target?.NodeId, problem.Target?.EntryId, problem.Target?.FieldId, problem.Target?.File, problem.InputText)).ToArray();
        Problems.Clear(); foreach (var problem in distinct) Problems.Add(problem);
        foreach (var group in _groups.Loaded)
            foreach (var entry in group.Entries)
            {
                var active = Problems.Where(problem => problem.InputText is null && (problem.IsError || problem.IsWarning) && (problem.Target?.Revision is null || problem.Target.Revision == _revision) && problem.Target?.NodeId == group.Document.Id && problem.Target?.EntryId == entry.StableId).ToArray();
                entry.ProblemCount = active.Length; entry.HasProblemErrors = active.Any(problem => problem.IsError);
            }
        OnPropertyChanged(nameof(Count)); OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(VisibleProblems));
    }
    [RelayCommand]
    private void Validate()
    {
        _checkCancellation?.Cancel(); ++_checkGeneration;
        var snapshot = _content.CaptureWorking();
        List<AuthorProblem> results;
        lock (_analysisGate) results = Analyze(snapshot, _projects.Current!.RootPath, _revision);
        CompleteValidation(results, _localization.Get(), snapshot, _revision);
    }
    private List<AuthorProblem> Analyze(EditorProjectDocument snapshot, string root, long revision)
    {
        var diagnostics = new List<ToolDiagnostic>();
        var validator = new ProjectValidationService(root);
        validator.ValidateGraph(JsonSerializer.SerializeToElement(snapshot.Graph), diagnostics, checkSourceReferences: false);
        var results = new List<AuthorProblem>();
        foreach (var diagnostic in diagnostics)
        {
            var segments = diagnostic.JsonPointer?.Split('/') ?? [];
            var nodeId = segments.Length > 2 && segments[1] == "nodes" && int.TryParse(segments[2], out var index) && index < snapshot.Graph.Nodes.Count ? snapshot.Graph.Nodes[index].Id : null;
            results.Add(new(diagnostic.Severity, diagnostic.Message, diagnostic.File ?? "Graph", new(nodeId, File: nodeId is null ? "Graph/graph.json" : null, Revision: revision)) { Code = diagnostic.Code, Details = diagnostic.Message });
        }
        foreach (var pair in snapshot.GroupEntries)
        {
            var node = snapshot.Graph.Nodes.FirstOrDefault(item => item.Id == pair.Key);
            var path = "Graph/" + (node?.File ?? $"groups/{pair.Key}.rawgalgroup");
            var raw = JsonSerializer.SerializeToElement(new { version = GalNet.Core.Serialization.GroupDocument.CurrentVersion, kind = "Raw", entries = pair.Value.Select(entry => new { id = entry.StableId, type = entry.Type, condition = entry.Condition, parameters = entry.Parameters }) });
            diagnostics.Clear();
            ProjectValidationService.ValidateGroupDocument(raw, path, diagnostics, _catalog);
            var fieldProblems = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in pair.Value)
                if (_catalog.TryGet(entry.Type, out var schema))
                    foreach (var field in schema.DynamicParameters.Fields)
                    {
                        var issue = entry.Parameters.TryGetValue(field.Id, out var value) ? ValueValidation.Validate(field.Value, value)
                            : field.IsRequired && field.DefaultValue is null ? new ValueIssue("field.required", "Field must be provided.") : null;
                        if (issue is null) continue;
                        fieldProblems.Add(entry.StableId);
                        results.Add(new("error", issue.Message, path, new(pair.Key, entry.StableId, field.Id, Revision: revision)) { Code = issue.Code, Field = field, Details = issue.Message });
                    }
            foreach (var diagnostic in diagnostics)
            {
                var segments = diagnostic.JsonPointer?.Split('/') ?? [];
                var index = segments.Length > 2 && int.TryParse(segments[2], out var parsed) ? parsed : -1;
                var entry = index >= 0 && index < pair.Value.Count ? pair.Value[index] : null;
                if (diagnostic.Code == "entry.parametersInvalid" && entry is not null && fieldProblems.Contains(entry.StableId)) continue;
                var field = segments.Length > 4 && segments[3] == "parameters" ? segments[4] : segments.LastOrDefault() == "condition" ? "condition" : null;
                results.Add(new(diagnostic.Severity, diagnostic.Message, path, new(pair.Key, diagnostic.ObjectId ?? entry?.StableId, field, Revision: revision)) { Code = diagnostic.Code, Details = diagnostic.Message });
            }
        }
        return results;
    }
    private void CompleteValidation(List<AuthorProblem> results, LocalizationReferenceIndex? localization, EditorProjectDocument snapshot, long revision)
    {
        _incomplete = localization?.Complete != true;
        foreach (var issue in localization?.Issues ?? [])
        {
            var node = snapshot.Graph.Nodes.FirstOrDefault(item => item.Id == issue.File);
            var segments = issue.JsonPointer.Split('/').Skip(1)
                .Select(segment => segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)).ToArray();
            var field = segments.FirstOrDefault();
            var fieldPath = segments.Skip(1).ToArray();
            results.Add(new(issue.Code is "schema.unavailable" or "field.unknown" or "field.opaque" or "analysis.limit" ? "warning" : "error", issue.Message, issue.File,
                node is not null ? new(node.Id, issue.EntryId, field, Revision: revision, FieldPath: fieldPath) : new(File: issue.File, Revision: revision))
            { Details = issue.Message, Code = "localization." + issue.Code, FieldPath = node is not null ? fieldPath : null });
        }
        _validation = results; _validatedRevision = revision; _checking = false; Refresh();
    }
    public void Report(string severity, string message, string source, AuthorNavigationTarget? target = null)
    { if (!_external.Any(problem => problem.Source == source && problem.Message == message)) _external.Add(new(severity, message, source, target)); Refresh(); }
    private string Source(AuthorNavigationTarget? target)
    {
        if (target?.NodeId is not { } nodeId) return target?.File ?? "";
        var group = _groups.Loaded.FirstOrDefault(group => group.Document.Id == nodeId);
        var node = _documents?.CurrentDocument.Nodes.FirstOrDefault(node => node.Id == nodeId);
        var entry = group?.Entries.FirstOrDefault(entry => entry.StableId == target.EntryId);
        var data = entry?.Source;
        if (data is null && _documents?.Groups.TryGetValue(nodeId, out var document) == true) data = document.Entries.FirstOrDefault(entry => entry.StableId == target.EntryId);
        var source = data is null ? node?.Name ?? ""
            : _text.Format("Problem.Source.Entry", node?.Name ?? "", data.Id,
                EditorProblemText.Lookup("Entry.Type." + data.Type, data.Type, _text));
        if (target.FieldId is { } id)
        {
            var field = entry?.DetailForm?.Fields.FirstOrDefault(field => field.Id == id);
            if (field is null && data is not null && _catalog.TryGet(data.Type, out var schema)) field = schema.DynamicParameters.Fields.FirstOrDefault(field => field.Id == id);
            source += " / " + (id == "condition" ? _text["Uiux.Entry.Condition"] : field is null ? EditorProblemText.Lookup("Entry.Parameter." + id, id, _text) : EditorProblemText.Label(field, _text));
        }
        return source;
    }
    private AuthorProblem Present(AuthorProblem problem)
    {
        var message = problem.Field is { } field && problem.Code is { } code
            ? EditorProblemText.Value(field, new(code, problem.Message), _text)
            : problem.Code switch
            {
                "condition.syntaxInvalid" => _text["Problem.Condition"],
                "entry.typeUnknown" => _text["Problem.Entry.UnknownType"],
                "entry.parametersInvalid" or "entry.compileInvalid" => _text["Problem.Entry.Compile"],
                "localization.field.opaque" => _text["Problem.Localization.Opaque"],
                { } other when !other.StartsWith("scene.", StringComparison.Ordinal) => _text["Problem.Generic"],
                _ => problem.Message
            };
        var source = Source(problem.Target);
        if (problem.FieldPath is { Count: > 0 } path)
            source += " / " + string.Join(" / ", path.Select((segment, depth) => int.TryParse(segment, out var index) ? "#" + (index + 1)
                : depth == path.Count - 1 && problem.Field is { } nestedField ? EditorProblemText.Label(nestedField, _text)
                : EditorProblemText.Lookup("Entry.Parameter." + segment, segment, _text)));
        var canNavigate = problem.Target is { } target && _navigation.CanNavigate(target);
        return problem with
        {
            Message = message,
            Source = source.Length > 0 ? source : problem.Source,
            CanNavigate = canNavigate,
            NavigationHint = _text[canNavigate ? "Problem.Navigate" : "Problem.Unavailable"]
        };
    }
    [RelayCommand]
    private void Navigate(AuthorProblem? problem)
    {
        if (problem?.Target is not { } target) return;
        if (!_navigation.Navigate(target)) { Refresh(); Report("info", _text["Problem.Unavailable"], problem.Source); }
    }
    [RelayCommand] private void Discard(AuthorProblem? problem) => ChangeArchive(problem?.Discard);
    [RelayCommand] private void Restore(AuthorProblem? problem) => ChangeArchive(problem?.Restore);
    private void ChangeArchive(Action? action)
    {
        try { action?.Invoke(); }
        catch (Exception failure) { Report("error", EditorText.Error(failure).Render(_text), ".galnet/editor/input-archive.json"); }
    }
    public void Dispose()
    {
        _disposed = true; ++_checkGeneration; _checkCancellation?.Cancel(); _checkCancellation?.Dispose();
        lock (_analysisGate) { } // Finish any contract callback before the project unloads its schema assembly.
        _edits.Changed -= Refresh; _histories.Changed -= ContentChanged; _content.ExternalReloaded -= ExternalReloaded;
        _content.Changed -= FilesChanged; _text.PropertyChanged -= LocaleChanged; if (_scenes is not null) _scenes.Changed -= SceneChanged;
    }
}
