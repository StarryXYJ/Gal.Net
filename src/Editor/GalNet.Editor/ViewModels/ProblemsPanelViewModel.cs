using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.ViewModels;

public sealed record AuthorProblem(string Severity, string Message, string Source, AuthorNavigationTarget? Target, Action? Discard = null)
{
    public SceneReferenceDiagnostic? SceneReference { get; init; }
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
    private string _signature;
    private bool _incomplete;
    [ObservableProperty] private bool _onlyErrors;
    public IEnumerable<AuthorProblem> VisibleProblems => Problems.Where(problem => !OnlyErrors || problem.IsError).OrderBy(problem => problem.IsError ? 0 : problem.IsWarning ? 1 : 2);
    partial void OnOnlyErrorsChanged(bool value) => OnPropertyChanged(nameof(VisibleProblems));
    private List<AuthorProblem> _validation = [];
    private long _revision;
    private long? _validatedRevision;
    public ObservableCollection<AuthorProblem> Problems { get; } = [];
    public int Count => Problems.Count;
    public string Status => _text[_validatedRevision is null ? "Uiux.Validation.Unchecked" : _validatedRevision != _revision ? "Uiux.Validation.Stale"
        : _validation.Any(problem => problem.Severity == "error") ? "Uiux.Validation.Failed" : _incomplete || _sceneDiagnostics.Any(diagnostic => diagnostic.Code is "scene.reference.uncertain" or "scene.coverage.unknown" or "scene.coverage.structure" or "scene.analysis.limit") ? "Uiux.Validation.Incomplete" : "Uiux.Validation.Passed"];
    public ProblemsPanelViewModel(EntryEditSession edits, IProjectContentSession content, IProjectService projects, IEntryCatalog catalog,
        EditorHistories histories, IEditorLocalizationService text, AuthorNavigationService navigation, EditorGroupProjectionStore groups, ProjectLocalizationQuery localization, ProjectSceneTargets? scenes = null)
    {
        _edits = edits; _content = content; _projects = projects; _catalog = catalog; _histories = histories; _text = text; _navigation = navigation;
        _groups = groups;
        _scenes = scenes;
        if (scenes is not null) scenes.Changed += SceneChanged;
        _localization = localization; _signature = JsonSerializer.Serialize(content.CaptureWorking());
        edits.Changed += Refresh; histories.Changed += ContentChanged; text.PropertyChanged += LocaleChanged; Refresh();
    }
    private void LocaleChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => Refresh();
    private void SceneChanged()
    {
        if (_validatedRevision is null || _scenes is null) return;
        _sceneDiagnostics = _scenes.Diagnostics(); Refresh();
    }
    private void ContentChanged()
    {
        var signature = JsonSerializer.Serialize(_content.CaptureWorking());
        if (signature != _signature)
        {
            _signature = signature; _revision++;
            if (_validatedRevision is not null && _scenes is not null) _sceneDiagnostics = _scenes.Diagnostics();
        }
        Refresh();
    }
    private void Refresh()
    {
        Problems.Clear();
        foreach (var draft in _edits.Pending)
            Problems.Add(new(draft.State.IsConflicted || draft.State.Issue is not null ? "error" : "info", draft.State.IsConflicted ? _text["Uiux.Draft.Conflict"] : draft.State.Issue?.Message ?? _text["Uiux.Draft.Pending"],
                $"{draft.Owner.Type} / {draft.Owner.Id} / {draft.Root.Id}", new(draft.Owner.AuthorGroupId, draft.Owner.StableId, draft.Root.Id, Revision: _revision), draft.State.Clear));
        foreach (var problem in _validation) Problems.Add(problem);
        foreach (var diagnostic in _sceneDiagnostics)
            Problems.Add(new("warning", _text["Scene.Diagnostic." + diagnostic.Code], diagnostic.Owner.GroupId + diagnostic.JsonPointer,
                new(diagnostic.Owner.GroupId, diagnostic.Owner.EntryId, diagnostic.JsonPointer.Split('/').ElementAtOrDefault(2), Revision: _revision))
            { SceneReference = diagnostic });
        foreach (var problem in _external) Problems.Add(problem);
        foreach (var group in _groups.Loaded)
            foreach (var entry in group.Entries) entry.ProblemCount = Problems.Count(problem => problem.Target?.NodeId == group.Document.Id && problem.Target?.EntryId == entry.StableId);
        OnPropertyChanged(nameof(Count)); OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(VisibleProblems));
    }
    [RelayCommand]
    private void Validate()
    {
        var snapshot = _content.CaptureWorking();
        _signature = JsonSerializer.Serialize(snapshot);
        var diagnostics = new List<ToolDiagnostic>();
        var validator = new ProjectValidationService(_projects.Current!.RootPath);
        validator.ValidateGraph(JsonSerializer.SerializeToElement(snapshot.Graph), diagnostics, checkSourceReferences: false);
        var results = new List<AuthorProblem>();
        foreach (var diagnostic in diagnostics) results.Add(new(diagnostic.Severity, diagnostic.Message, diagnostic.File ?? "Graph", new(diagnostic.ObjectId, Revision: _revision)));
        foreach (var pair in snapshot.GroupEntries)
        {
            var node = snapshot.Graph.Nodes.FirstOrDefault(item => item.Id == pair.Key);
            var path = "Graph/" + (node?.File ?? $"groups/{pair.Key}.rawgalgroup");
            var raw = JsonSerializer.SerializeToElement(new { version = GalNet.Core.Serialization.GroupDocument.CurrentVersion, kind = "Raw", entries = pair.Value.Select(entry => new { id = entry.StableId, type = entry.Type, condition = entry.Condition, parameters = entry.Parameters }) });
            diagnostics.Clear();
            ProjectValidationService.ValidateGroupDocument(raw, path, diagnostics, _catalog);
            foreach (var diagnostic in diagnostics)
            {
                var segments = diagnostic.JsonPointer?.Split('/') ?? [];
                var index = segments.Length > 2 && int.TryParse(segments[2], out var parsed) ? parsed : -1;
                var entry = index >= 0 && index < pair.Value.Count ? pair.Value[index] : null;
                var field = segments.Length > 4 && segments[3] == "parameters" ? segments[4] : null;
                results.Add(new(diagnostic.Severity, diagnostic.Message, path, new(pair.Key, diagnostic.ObjectId ?? entry?.StableId, field, Revision: _revision)));
            }
            foreach (var entry in pair.Value)
                if (_catalog.TryGet(entry.Type, out var schema) && schema.DynamicParameters is { } parameters)
                    foreach (var field in parameters.Fields)
                    {
                        var issue = entry.Parameters.TryGetValue(field.Id, out var value) ? GalNet.Core.Parameters.ValueValidation.Validate(field.Value, value)
                            : field.IsRequired && field.DefaultValue is null ? new GalNet.Core.Parameters.ValueIssue("field.required", _text["Uiux.Draft.Invalid"]) : null;
                        if (issue is not null) results.Add(new("error", issue.Message, path, new(pair.Key, entry.StableId, field.Id, Revision: _revision)));
                    }
        }
        var localization = _localization.Get(); _incomplete = localization?.Complete != true;
        foreach (var issue in localization?.Issues ?? [])
        {
            var reference = localization!.References.FirstOrDefault(item => item.Owner == issue.File && item.JsonPointer == issue.JsonPointer);
            var node = snapshot.Graph.Nodes.FirstOrDefault(item => item.Id == issue.File);
            var field = issue.JsonPointer.Split('/').LastOrDefault();
            results.Add(new(issue.Code is "schema.unavailable" or "field.unknown" or "field.opaque" or "analysis.limit" ? "warning" : "error", issue.Message, issue.File,
                node is not null ? new(node.Id, reference?.EntryId, field, Revision: _revision) : new(File: issue.File, Revision: _revision)));
        }
        _validation = results; _validatedRevision = _revision; Refresh();
        SceneChanged();
    }
    public void Report(string severity, string message, string source, AuthorNavigationTarget? target = null)
    { if (!_external.Any(problem => problem.Source == source && problem.Message == message)) _external.Add(new(severity, message, source, target)); Refresh(); }
    [RelayCommand] private void Navigate(AuthorProblem? problem) { if (problem?.Target is { } target) _navigation.Navigate(target); }
    [RelayCommand] private void Discard(AuthorProblem? problem) => problem?.Discard?.Invoke();
    public void Dispose() { _edits.Changed -= Refresh; _histories.Changed -= ContentChanged; _text.PropertyChanged -= LocaleChanged; if (_scenes is not null) _scenes.Changed -= SceneChanged; }
}
