using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Services.Interfaces;

namespace GalNet.Editor.ViewModels;

public sealed partial class ExportPanelViewModel : ObservableObject
{
    private readonly IProjectService _projects;
    private readonly IFileDialogService _dialogs;
    private readonly IGameExportService _exporter;

    public IEditorLocalizationService L { get; }
    public IReadOnlyList<ExportModeOption> Modes { get; }

    [ObservableProperty] private string _outputDirectory = "";
    [ObservableProperty] private bool _isExporting;
    [ObservableProperty] private ExportModeOption _selectedMode;

    public string PackageName => $"{_projects.Current?.Name ?? "game"}.galpak";
    public string PackagePath => Path.Combine(OutputDirectory, IsPackageMode ? PackageName : "pak");
    public bool IsPackageMode => SelectedMode.Key == "package";

    public ExportPanelViewModel(IProjectService projects, IFileDialogService dialogs, IGameExportService exporter, IEditorLocalizationService localization)
    {
        _projects = projects;
        _dialogs = dialogs;
        _exporter = exporter;
        L = localization;
        OutputDirectory = projects.Current?.OutputPath ?? "";
        Modes =
        [
            new("package", L["Export.Mode.Galpak"]),
            new("directory", L["Export.Mode.Directory"])
        ];
        _selectedMode = Modes[0];
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var path = await _dialogs.OpenFolderPickerAsync(L["Export.SelectDirectory"]);
        if (!string.IsNullOrWhiteSpace(path)) OutputDirectory = path;
    }

    public async Task<GameExportResult> ExportAsync(CancellationToken cancellationToken)
    {
        if (IsExporting || string.IsNullOrWhiteSpace(OutputDirectory) || _projects.Current is null
            || !Modes.Contains(SelectedMode))
            return GameExportResult.Failed(L["Export.Unavailable"]);

        IsExporting = true;
        try
        {
            return IsPackageMode
                ? await _exporter.ExportAsync(OutputDirectory, cancellationToken)
                : await _exporter.ExportDirectoryAsync(PackagePath, cancellationToken);
        }
        catch (Exception error) { return GameExportResult.Failed(error.Message); }
        finally { IsExporting = false; }
    }

    partial void OnOutputDirectoryChanged(string value) => OnPropertyChanged(nameof(PackagePath));
    partial void OnSelectedModeChanged(ExportModeOption value)
    {
        OnPropertyChanged(nameof(IsPackageMode));
        OnPropertyChanged(nameof(PackagePath));
    }
}

public sealed record ExportModeOption(string Key, string Label)
{
    public override string ToString() => Label;
}
