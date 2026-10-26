using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Contracts.Builtins;
using GalNet.Core.Configuration;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Game.Controls.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.ViewModels;

public partial class ProjectSettingsPanelViewModel : ObservableObject, IDisposable, IUndoRedoTarget
{
    private readonly IProjectService _projects;
    private readonly EditorHistories _histories;
    private readonly IProjectContentSession _session;
    private readonly IDisposable _registration;
    private GalProject? _project;
    private string _originalName = "";
    private bool _loading, _disposed;
    private Exception? _validationError;
    public ObservableCollection<ConfigurationSectionEditor> Sections { get; } = [];
    public EditorRegistry FieldEditors { get; }
    public IUndoRedoHistory UndoRedoHistory => _histories.Content;
    public IEditorLocalizationService L { get; }
    public event Action? DraftReset;
    [ObservableProperty] private string _projectName = "";
    [ObservableProperty] private string _isDirtyText = "";
    [ObservableProperty] private string _debugExecutable = "";
    [ObservableProperty] private string _debugArguments = "";
    [ObservableProperty] private string _debugWorkingDirectory = "";
    [ObservableProperty] private string _debugConfigurationMessage = "";

    public ProjectSettingsPanelViewModel(IProjectService projects, EditorHistories histories,
        IEditorLocalizationService localization, IProjectContentSession session, EditorRegistry fieldEditors)
    {
        _projects = projects; _histories = histories; _session = session; L = localization; FieldEditors = fieldEditors;
        _registration = session.RegisterDraft(() => HasDraft, CommitDraftAsync, LoadFromProject);
        projects.CurrentChanged += OnProjectChanged;
        histories.Content.Changed += OnHistoryChanged;
        session.ExternalReloaded += LoadFromProject;
        L.PropertyChanged += OnLocalizationChanged;
        LoadFromProject();
    }

    private bool IsCurrent => !_disposed && _project is not null && ReferenceEquals(_project, _projects.Current);
    private bool HasDraft => IsCurrent && (ProjectName != _originalName || Sections.Any(section => section.HasChanges));
    partial void OnProjectNameChanged(string value) => RefreshDirty();
    private void RefreshDirty()
    {
        if (_loading || _disposed) return;
        UpdateStatus();
        _session.Refresh();
    }
    private void UpdateStatus() => IsDirtyText = _validationError is { } error
        ? L.Format("Settings.Project.ApplyFailed", EditorText.Error(error).Render(L))
        : HasDraft || _histories.Content.IsDirty ? L["Settings.Project.Unsaved"] : "";
    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    { if (args.PropertyName == "Item[]") UpdateStatus(); }
    private void OnProjectChanged(GalProject? _) => LoadFromProject();
    private void OnHistoryChanged() { if (!_histories.Content.IsBusy && !HasDraft) LoadFromProject(); }
    private void LoadFromProject()
    {
        if (_disposed) return;
        _loading = true;
        _validationError = null;
        foreach (var section in Sections) section.Dispose();
        Sections.Clear();
        _project = _projects.Current;
        _originalName = _project?.Name ?? ""; ProjectName = _originalName;
        DebugExecutable = DebugArguments = DebugWorkingDirectory = DebugConfigurationMessage = "";
        if (_project is { } project)
        {
            foreach (var section in project.Configurations.Sections)
                Sections.Add(new(section, project.Configuration, FieldEditors, () => IsCurrent, RefreshDirty,
                    field => section.Id != DefaultProjectConfiguration.Section.Id || field.Id is not ("targetLocale" or "availableLocales")));
            try
            {
                if (ExternalGameDebugClient.LoadSettings(project.RootPath) is { } launch)
                {
                    DebugExecutable = launch.Executable; DebugArguments = string.Join(Environment.NewLine, launch.Arguments ?? []);
                    DebugWorkingDirectory = launch.WorkingDirectory ?? "";
                }
            }
            catch (Exception error) when (error is System.IO.IOException or JsonException or UnauthorizedAccessException)
            { DebugConfigurationMessage = error.Message; }
        }
        _loading = false; RefreshDirty(); DraftReset?.Invoke();
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try { await CommitDraftAsync(); }
        catch (Exception error) { _validationError = error; UpdateStatus(); }
    }
    public async Task CommitDraftAsync()
    {
        if (!HasDraft) return;
        try
        {
            if (string.IsNullOrWhiteSpace(ProjectName)) throw new EditorOperationException("project.nameRequired", "Project name is required.");
            var project = _project!;
            foreach (var section in Sections) await section.FlushAsync();
            if (!IsCurrent) throw new EditorOperationException("settings.obsolete", "The project was closed.");
            var next = project.Configuration.Clone();
            foreach (var section in Sections) section.Apply(next);
            var graph = project.Services.GetRequiredService<IEditorDocumentService>().CurrentDocument;
            var rename = ProjectName.Trim() != _originalName;
            if (rename && graph.Name != _originalName) throw new EditorOperationException("settings.changed", "Project name changed. Cancel the draft and retry.");
            var changes = Sections.Where(section => section.HasChanges).Select(section => section.Id).Distinct()
                .Select(id => new SectionChange(id, project.Configuration.Sections.GetValueOrDefault(id), next.Sections.GetValueOrDefault(id))).ToArray();
            if (rename || changes.Length != 0)
                await _histories.Content.ExecuteAsync(new SettingsEdit(project.Configuration, graph, graph.Name,
                    rename ? ProjectName.Trim() : graph.Name, changes));
            LoadFromProject();
        }
        catch (Exception error)
        {
            _validationError = error; UpdateStatus();
            throw;
        }
    }
    [RelayCommand] private void CancelDraft() => LoadFromProject();
    [RelayCommand]
    private void SaveDebugConfiguration()
    {
        if (!IsCurrent) return;
        try
        {
            ExternalGameDebugClient.SaveSettings(_project!.RootPath, new(DebugExecutable.Trim(),
                DebugArguments.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries),
                string.IsNullOrWhiteSpace(DebugWorkingDirectory) ? null : DebugWorkingDirectory.Trim()));
            DebugConfigurationMessage = "";
        }
        catch (Exception error) { DebugConfigurationMessage = error.Message; }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _registration.Dispose(); _projects.CurrentChanged -= OnProjectChanged;
        _histories.Content.Changed -= OnHistoryChanged; _session.ExternalReloaded -= LoadFromProject;
        L.PropertyChanged -= OnLocalizationChanged;
        foreach (var section in Sections) section.Dispose();
        Sections.Clear(); _project = null;
        GC.SuppressFinalize(this);
    }

    private sealed record SectionChange(string Id, ProjectSectionData? Before, ProjectSectionData? After);
    private sealed record SettingsEdit(ProjectConfiguration Configuration, EditorGraphDocument Graph,
        string BeforeName, string AfterName, SectionChange[] Changes) : IUndoableEdit
    {
        public string Description => "Change project name and settings";
        public long RetainedBytes => JsonSerializer.SerializeToUtf8Bytes(Changes).LongLength + (BeforeName.Length + AfterName.Length) * 2L;
        public void Undo() => Apply(false);
        public void Redo() => Apply(true);
        private void Apply(bool forward)
        {
            if (BeforeName != AfterName && Graph.Name != (forward ? BeforeName : AfterName) ||
                Changes.Any(change => !ConfigurationSectionEditor.Equal(Configuration.Sections.GetValueOrDefault(change.Id), forward ? change.Before : change.After)))
                throw new EditorOperationException("settings.changed", "Project settings changed. Review the current values and retry.");
            var next = Configuration.Clone();
            foreach (var change in Changes)
            {
                if ((forward ? change.After : change.Before) is { } value) next.SetRaw(change.Id, value);
                else next.Remove(change.Id);
            }
            Configuration.ReplaceFrom(next);
            if (BeforeName != AfterName) Graph.Name = forward ? AfterName : BeforeName;
        }
    }
}
