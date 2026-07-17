using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Abstraction.Sessions;
using Serilog;

namespace GalNet.Editor.ViewModels;

public partial class ProjectSettingsPanelViewModel : ObservableObject, IDisposable
{
    public IReadOnlyList<string> ResolutionPresets => NewProjectPanelViewModel.ResolutionPresets;
    private readonly IProjectService _projectService;
    private readonly IEditorSession _session;
    private readonly IEditorSettingsService _editorSettings;
    private bool _isLoading;

    public IEditorLocalizationService L { get; }

    [ObservableProperty]
    private int _defaultWidth = 1920;

    [ObservableProperty]
    private int _defaultHeight = 1080;

    [ObservableProperty]
    private int _saveSlotCount = 60;

    [ObservableProperty]
    private int _sfxChannelCount = 4;

    [ObservableProperty]
    private string _targetLocale = "zh-CN";

    [ObservableProperty]
    private string _isDirtyText = "";

    [ObservableProperty]
    private string _resolution = "1920×1080";

    public ProjectSettingsPanelViewModel(
        IProjectService projectService,
        IEditorSession session,
        IEditorSettingsService editorSettings,
        IEditorLocalizationService localization)
    {
        _projectService = projectService;
        _session = session;
        _editorSettings = editorSettings;
        L = localization;
        L.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "Item[]")
                UpdateDirtyText();
        };

        _session.DocumentChanged += LoadFromProject;
        _session.HistoryChanged += UpdateDirtyText;
        LoadFromProject();
    }

    public void Dispose()
    {
        _session.DocumentChanged -= LoadFromProject;
        _session.HistoryChanged -= UpdateDirtyText;
    }

    private void LoadFromProject()
    {
        var settings = _session.Document.Settings;

        _isLoading = true;
        DefaultWidth = settings.DefaultWidth;
        DefaultHeight = settings.DefaultHeight;
        Resolution = $"{DefaultWidth}×{DefaultHeight}";
        SaveSlotCount = settings.SaveSlotCount;
        SfxChannelCount = settings.SfxChannelCount;
        TargetLocale = settings.TargetLocale.Code;
        _isLoading = false;
        UpdateDirtyText();
    }

    private void UpdateDirtyText()
    {
        IsDirtyText = _session.IsDirty ? L["Settings.Project.Unsaved"] : "";
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            if (_projectService.Current is not { } project) return;
            var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["defaultWidth"] = JsonSerializer.SerializeToElement(DefaultWidth),
                ["defaultHeight"] = JsonSerializer.SerializeToElement(DefaultHeight),
                ["saveSlotCount"] = JsonSerializer.SerializeToElement(SaveSlotCount),
                ["sfxChannelCount"] = JsonSerializer.SerializeToElement(SfxChannelCount),
                ["targetLocale"] = JsonSerializer.SerializeToElement(TargetLocale)
            };
            var result = _session.Execute(
                new PatchProjectSettingsCommand(values),
                new ExecuteOptions(MergeKey: "project:settings", MergeWindow: TimeSpan.FromSeconds(1)));
            if (!result.Success)
                throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Select(item => item.Message)));

            CopySettings(_session.Document.Settings, project.Settings);
            project.IsDirty = _session.IsDirty;
            UpdateDirtyText();
            Log.Information("Project settings updated through command session");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            IsDirtyText = L["Settings.SaveFailed"];
            Log.Error(ex, "Failed to save project settings");
        }
    }

    partial void OnDefaultWidthChanged(int value) => TriggerAutoSave();
    partial void OnDefaultHeightChanged(int value) => TriggerAutoSave();
    partial void OnResolutionChanged(string value)
    {
        if (_isLoading || !NewProjectPanelViewModel.TryParseResolution(value, out var width, out var height)) return;
        _isLoading = true; DefaultWidth = width; DefaultHeight = height; _isLoading = false; TriggerAutoSave();
    }
    partial void OnSaveSlotCountChanged(int value) => TriggerAutoSave();
    partial void OnSfxChannelCountChanged(int value) => TriggerAutoSave();
    partial void OnTargetLocaleChanged(string value) => TriggerAutoSave();

    private void TriggerAutoSave()
    {
        if (_isLoading || _projectService.Current is null)
            return;
        _ = SaveSettingsAsync();
    }

    private static void CopySettings(Core.Settings.ProjectSettings source, Core.Settings.ProjectSettings target)
    {
        target.DefaultWidth = source.DefaultWidth;
        target.DefaultHeight = source.DefaultHeight;
        target.SaveSlotCount = source.SaveSlotCount;
        target.SfxChannelCount = source.SfxChannelCount;
        target.TargetLocale = new Core.I18n.I18nLocale(source.TargetLocale.Code);
        target.AvailableLocales = source.AvailableLocales
            .Select(locale => new Core.I18n.I18nLocale(locale.Code))
            .ToList();
        target.PlayerVariables = source.PlayerVariables.Select(item => item.Clone()).ToList();
        target.SaveVariables = source.SaveVariables.Select(item => item.Clone()).ToList();
    }
}
