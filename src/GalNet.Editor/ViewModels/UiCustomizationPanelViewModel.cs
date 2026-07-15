using System;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.UI;
using GalNet.Editor.Abstraction.Services;
using Serilog;

namespace GalNet.Editor.ViewModels;

public sealed partial class UiCustomizationPanelViewModel : ObservableObject, IDisposable
{
    private readonly IProjectService _projectService;
    private readonly EditorWorkspaceViewModel _workspace;
    private readonly IEditorLocalizationService _localization;
    private string _statusKey = "UiCustomization.Status.NoProject";

    [ObservableProperty] private UiProject? _configuration;
    [ObservableProperty] private string _statusText = string.Empty;

    public UiCustomizationPanelViewModel(IProjectService projectService, EditorWorkspaceViewModel workspace, IEditorLocalizationService localization)
    {
        _projectService = projectService;
        _workspace = workspace;
        _localization = localization;
        _projectService.CurrentChanged += OnProjectChanged;
        _localization.PropertyChanged += OnLocalizationChanged;
        LoadProject();
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (_projectService.Current is not { } project || Configuration is null)
            return;

        try
        {
            project.UiProject.NotifyChanged();
            project.IsDirty = true;
            await project.UiProject.SaveAsync();
            project.IsDirty = false;
            SetStatus("UiCustomization.Status.Restarting");

            if (_workspace.ActivePreview is { } preview)
                await preview.RestartAsync();

            SetStatus("UiCustomization.Status.Applied");
        }
        catch (Exception ex)
        {
            SetStatus("UiCustomization.Status.SaveFailed");
            Log.Error(ex, "Failed to save UI customization");
        }
    }

    private void OnProjectChanged(Abstraction.Project.GalProject? _) => LoadProject();

    private void LoadProject()
    {
        Configuration = _projectService.Current?.UiProject.Current;
        SetStatus(Configuration is null ? "UiCustomization.Status.NoProject" : "UiCustomization.Status.Ready");
    }

    private void SetStatus(string key)
    {
        _statusKey = key;
        StatusText = _localization[key];
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IEditorLocalizationService.CurrentCulture) or "Item[]")
            StatusText = _localization[_statusKey];
    }

    public void Dispose()
    {
        _projectService.CurrentChanged -= OnProjectChanged;
        _localization.PropertyChanged -= OnLocalizationChanged;
    }
}
