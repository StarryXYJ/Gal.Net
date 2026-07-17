using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Services.Interfaces;
namespace GalNet.Editor.ViewModels;
public sealed partial class ExportPanelViewModel : ObservableObject
{
    private readonly IProjectService _projects; private readonly IFileDialogService _dialogs; private readonly IGameExportService _exporter;
    public IEditorLocalizationService L { get; }
    [ObservableProperty] private string _outputDirectory = ""; [ObservableProperty] private bool _isExporting;
    public string PackageName => $"{_projects.Current?.Name ?? "game"}.galpak"; public string PackagePath => Path.Combine(OutputDirectory, PackageName);
    public ExportPanelViewModel(IProjectService projects, IFileDialogService dialogs, IGameExportService exporter, IEditorLocalizationService localization) { _projects = projects; _dialogs = dialogs; _exporter = exporter; L = localization; OutputDirectory = projects.Current?.OutputPath ?? ""; }
    [RelayCommand] private async Task BrowseAsync() { var path = await _dialogs.OpenFolderPickerAsync(L["Export.SelectDirectory"]); if (!string.IsNullOrWhiteSpace(path)) OutputDirectory = path; }
    public async Task<GameExportResult> ExportAsync(CancellationToken cancellationToken)
    {
        if (!CanExport()) return GameExportResult.Failed(L["Export.Unavailable"]);
        IsExporting = true;
        try { return await _exporter.ExportAsync(OutputDirectory, cancellationToken); }
        catch (Exception ex) { return GameExportResult.Failed(ex.Message); }
        finally { IsExporting = false; }
    }
    private bool CanExport() => !IsExporting && !string.IsNullOrWhiteSpace(OutputDirectory) && _projects.Current is not null;
    partial void OnOutputDirectoryChanged(string value) => OnPropertyChanged(nameof(PackagePath));
}
