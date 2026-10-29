using System.Diagnostics;
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
    private readonly string? _repositoryRoot;

    public IEditorLocalizationService L { get; }
    public IReadOnlyList<ExportModeOption> Modes { get; }

    [ObservableProperty] private string _outputDirectory = "";
    [ObservableProperty] private string _releaseOutputRoot = "";
    [ObservableProperty] private string _releaseVersion = "0.1.0-dev";
    [ObservableProperty] private string _galpakPath = "";
    [ObservableProperty] private bool _isExporting;
    [ObservableProperty] private ExportModeOption _selectedMode;

    public string PackageName => $"{_projects.Current?.Name ?? "game"}.galpak";
    public string PackagePath => IsPackageMode ? Path.Combine(OutputDirectory, PackageName) : ReleasePackagePath;
    public string ReleasePackagePath
    {
        get
        {
            var prefix = SelectedMode.Key switch
            {
                "editor" => "GalNet-Editor",
                "sample" => "GalNet-Sample",
                "sample-integrated" => "GalNet-Sample-GameTestCase",
                _ => "GalNet-Package"
            };
            return Path.Combine(ReleaseOutputRoot, ReleaseVersion, $"{prefix}-{ReleaseVersion}-win-x64.zip");
        }
    }

    public bool IsPackageMode => SelectedMode.Key == "package";
    public bool IsReleaseMode => !IsPackageMode;
    public bool IsIntegratedSample => SelectedMode.Key == "sample-integrated";

    public ExportPanelViewModel(IProjectService projects, IFileDialogService dialogs, IGameExportService exporter, IEditorLocalizationService localization)
    {
        _projects = projects;
        _dialogs = dialogs;
        _exporter = exporter;
        L = localization;
        _repositoryRoot = FindRepositoryRoot(projects.Current?.RootPath);
        OutputDirectory = projects.Current?.OutputPath ?? "";
        ReleaseOutputRoot = _repositoryRoot is null ? "" : Path.Combine(_repositoryRoot, "out", "release");
        Modes =
        [
            new("package", L["Export.Mode.Galpak"]),
            new("editor", L["Export.Mode.Editor"]),
            new("sample", L["Export.Mode.Sample"]),
            new("sample-integrated", L["Export.Mode.IntegratedSample"])
        ];
        _selectedMode = Modes[0];
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var path = await _dialogs.OpenFolderPickerAsync(L["Export.SelectDirectory"]);
        if (string.IsNullOrWhiteSpace(path)) return;
        if (IsPackageMode) OutputDirectory = path;
        else ReleaseOutputRoot = path;
    }

    [RelayCommand]
    private async Task BrowseGalpakAsync()
    {
        var paths = await _dialogs.OpenFilePickerAsync(L["Export.SelectGalpak"], allowMultiple: false);
        if (paths.Count > 0 && Path.GetExtension(paths[0]).Equals(".galpak", StringComparison.OrdinalIgnoreCase))
            GalpakPath = paths[0];
    }

    public async Task<GameExportResult> ExportAsync(CancellationToken cancellationToken)
    {
        if (IsPackageMode)
        {
            if (!CanExportPackage()) return GameExportResult.Failed(L["Export.Unavailable"]);
            IsExporting = true;
            try { return await _exporter.ExportAsync(OutputDirectory, cancellationToken); }
            catch (Exception ex) { return GameExportResult.Failed(ex.Message); }
            finally { IsExporting = false; }
        }

        if (!CanPublish()) return GameExportResult.Failed(L["Export.ReleaseUnavailable"]);
        IsExporting = true;
        try { return await PublishAsync(cancellationToken); }
        catch (OperationCanceledException) { return GameExportResult.Failed("Release packaging canceled."); }
        catch (Exception ex) { return GameExportResult.Failed(ex.Message); }
        finally { IsExporting = false; }
    }

    private async Task<GameExportResult> PublishAsync(CancellationToken cancellationToken)
    {
        var script = Path.Combine(_repositoryRoot!, "scripts", "release", "Publish.ps1");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };
        foreach (var argument in new[]
                 {
                     "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script,
                     "-Package", SelectedMode.Key switch
                     {
                         "editor" => "Editor",
                         "sample" => "Sample",
                         "sample-integrated" => "SampleIntegrated",
                         _ => throw new InvalidOperationException("Unsupported release package type.")
                     },
                     "-Version", ReleaseVersion,
                     "-OutputRoot", ReleaseOutputRoot
                 })
            process.StartInfo.ArgumentList.Add(argument);

        if (IsIntegratedSample)
        {
            if (string.IsNullOrWhiteSpace(GalpakPath))
            {
                process.StartInfo.ArgumentList.Add("-ProjectDirectory");
                process.StartInfo.ArgumentList.Add(_projects.Current!.RootPath);
            }
            else
            {
                process.StartInfo.ArgumentList.Add("-Galpak");
                process.StartInfo.ArgumentList.Add(GalpakPath);
            }
        }

        process.Start();
        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await stdoutTask;
        var error = await stderrTask;
        if (process.ExitCode != 0)
        {
            var details = string.Join(Environment.NewLine, new[] { output.Trim(), error.Trim() }.Where(value => value.Length > 0));
            return GameExportResult.Failed(string.IsNullOrWhiteSpace(details) ? $"Publish script failed with exit code {process.ExitCode}." : details);
        }
        return GameExportResult.Succeeded(ReleasePackagePath);
    }

    private bool CanExportPackage() => !IsExporting && !string.IsNullOrWhiteSpace(OutputDirectory) && _projects.Current is not null;

    private bool CanPublish() => !IsExporting && _projects.Current is not null && _repositoryRoot is not null &&
        File.Exists(Path.Combine(_repositoryRoot, "scripts", "release", "Publish.ps1")) &&
        !string.IsNullOrWhiteSpace(ReleaseOutputRoot) && !string.IsNullOrWhiteSpace(ReleaseVersion) &&
        (!IsIntegratedSample || string.IsNullOrWhiteSpace(GalpakPath) || File.Exists(GalpakPath));

    private static string? FindRepositoryRoot(string? startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath)) return null;
        var current = new DirectoryInfo(Path.GetFullPath(startPath));
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "scripts", "release", "Publish.ps1"))) return current.FullName;
            current = current.Parent;
        }
        return null;
    }

    partial void OnOutputDirectoryChanged(string value) => OnPropertyChanged(nameof(PackagePath));
    partial void OnReleaseOutputRootChanged(string value)
    {
        OnPropertyChanged(nameof(ReleasePackagePath));
        OnPropertyChanged(nameof(PackagePath));
    }
    partial void OnReleaseVersionChanged(string value)
    {
        OnPropertyChanged(nameof(ReleasePackagePath));
        OnPropertyChanged(nameof(PackagePath));
    }
    partial void OnGalpakPathChanged(string value) => OnPropertyChanged(nameof(IsIntegratedSample));
    partial void OnSelectedModeChanged(ExportModeOption value)
    {
        OnPropertyChanged(nameof(IsPackageMode));
        OnPropertyChanged(nameof(IsReleaseMode));
        OnPropertyChanged(nameof(IsIntegratedSample));
        OnPropertyChanged(nameof(PackagePath));
    }
}

public sealed record ExportModeOption(string Key, string Label)
{
    public override string ToString() => Label;
}
