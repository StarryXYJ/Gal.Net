using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GalNet.Core.Settings;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Abstraction.Commands;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GalNet.Editor.Shared.Services;

/// <summary>
/// 项目管理服务 —— 编辑器全局单例。
/// </summary>
public sealed class ProjectService : IProjectService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly IServiceProvider _globalServices;
    private readonly IEditorSettingsService _editorSettingsService;
    private readonly IProjectRecycleBin _recycleBin;

    private GalProject? _current;

    public GalProject? Current => _current;

    public event Action<GalProject?>? CurrentChanged;

    public ProjectService(IServiceProvider globalServices, IEditorSettingsService editorSettingsService, IProjectRecycleBin recycleBin)
    {
        _globalServices = globalServices;
        _editorSettingsService = editorSettingsService;
        _recycleBin = recycleBin;
        // 延迟加载，避免构造函数中执行同步 IO
    }

    // ────────── 编辑器设置持久化 ──────────

    // ────────── 项目管理 ──────────

    public async Task<GalProject> OpenAsync(string projectPath)
    {
        if (_current != null)
            await CloseAsync();

        projectPath = NormalizeProjectPath(projectPath);

        if (!Directory.Exists(projectPath))
            throw new DirectoryNotFoundException($"Project directory not found: {projectPath}");

        var scope = _globalServices.CreateScope();
        ProjectSettings settings;
        EditorProjectState editorState;
        string name;
        try
        {
            scope.ServiceProvider.GetRequiredService<ProjectSchemaSession>().Open(projectPath);
            settings = Commands.DirectProjectPersistence.LoadSettings(projectPath);
            editorState = await LoadEditorProjectStateAsync(projectPath);
            name = scope.ServiceProvider.GetRequiredService<IEditorDocumentRepository>().Load(projectPath, "", settings).Document.Name;
        }
        catch { scope.Dispose(); throw; }

        var id = Path.GetFileName(projectPath);

        var program = new GalProject(id, name, projectPath, settings, editorState, scope);

        _current = program;

        Log.Information("Project opened: {Name} at {Path}", name, projectPath);
        try
        {
            CurrentChanged?.Invoke(program);
            AddToRecentProjects(name, projectPath);
            _editorSettingsService.SaveSettings();
        }
        catch { await CloseAsync(); throw; }

        return program;
    }

    public async Task<GalProject> CreateAsync(string projectPath, string name, ProjectSettings settings, string contractsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractsDirectory);
        await Commands.EditorProjectCreator.CreateAsync(new(projectPath, name, contractsDirectory), settings);
        return await OpenAsync(projectPath);
    }
    public Task CloseAsync() => CloseCoreAsync(false);

    public Task<ToolResult> DeleteAsync(string projectPath, bool dryRun = false, System.Threading.CancellationToken cancellationToken = default) =>
        new Commands.ProjectDeleteService(_recycleBin).DeleteAsync(projectPath, dryRun, async root =>
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (_current is not null && string.Equals(_current.RootPath, root, comparison)) await CloseCoreAsync(true);
        }, RemoveRecentProject, cancellationToken);

    private async Task CloseCoreAsync(bool requireSuccess)
    {
        if (_current == null)
            return;

        var closingProject = _current;
        var failures = new List<Exception>();
        Log.Information("Closing project: {Name}", closingProject.Name);

        // Scoped services can raise notifications while being disposed. Clear the
        // current reference first so those notifications cannot resolve a disposed scope.
        _current = null;
        // The workspace still owns Dock/Inspector event handlers. Let it clear its
        // selection while the project scope remains valid; otherwise the inspector
        // can attempt to resolve a view model from an already disposed provider.
        try
        {
            CurrentChanged?.Invoke(null);
        }
        catch (Exception ex)
        {
            failures.Add(ex);
            Log.Warning(ex, "A project-change listener failed while closing {Name}", closingProject.Name);
        }
        try
        {
            await closingProject.DisposeAsync();
        }
        catch (Exception ex)
        {
            failures.Add(ex);
            // Scope disposal is guaranteed by GalProject even when preview shutdown fails.
            Log.Warning(ex, "Project cleanup reported an error while closing {Name}", closingProject.Name);
        }
        if (requireSuccess && failures.Count != 0) throw new AggregateException("Project shutdown failed; recycling was refused.", failures);
    }

    public Task SaveAsync()
    {
        if (_current == null)
            return Task.CompletedTask;

        return Task.WhenAll(
            SaveProjectSettingsAsync(_current.RootPath, _current.Settings),
            SaveEditorProjectStateAsync(_current.RootPath, _current.EditorState));
    }

    // ────────── 最近项目 ──────────

    public IReadOnlyList<RecentProjectInfo> GetRecentProjects()
    {
        return _editorSettingsService.GetSettings().RecentProjects
            .OrderByDescending(r => r.LastOpened)
            .ToList()
            .AsReadOnly();
    }

    public void RemoveRecentProject(string projectPath)
    {
        var normalizedPath = NormalizeProjectPath(projectPath);
        _editorSettingsService.GetSettings().RecentProjects.RemoveAll(r =>
            string.Equals(NormalizeProjectPath(r.Path), normalizedPath, StringComparison.OrdinalIgnoreCase));
        _editorSettingsService.SaveSettings();
    }

    public Task<bool> CheckUnsavedChangesAsync()
    {
        return Task.FromResult(_current?.IsDirty == true);
    }

    private void AddToRecentProjects(string name, string projectPath)
    {
        var settings = _editorSettingsService.GetSettings();
        settings.RecentProjects.RemoveAll(r =>
            string.Equals(
                NormalizeProjectPath(r.Path),
                projectPath,
                StringComparison.OrdinalIgnoreCase));

        settings.RecentProjects.Add(new RecentProjectInfo
        {
            Name = name,
            Path = projectPath,
            LastOpened = DateTime.Now
        });

        while (settings.RecentProjects.Count > settings.MaxRecentProjects)
            settings.RecentProjects.RemoveAt(0);
    }

    // ────────── 项目设置持久化 ──────────

    private static async Task SaveProjectSettingsAsync(string projectPath, ProjectSettings settings)
    {
        var settingsPath = Path.Combine(projectPath, "settings.json");
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(settingsPath, json);
    }

    private static async Task<EditorProjectState> LoadEditorProjectStateAsync(string projectPath)
    {
        var statePath = Path.Combine(projectPath, ".galnet", "editor-state.json");
        if (File.Exists(statePath))
        {
            var json = await File.ReadAllTextAsync(statePath);
            return JsonSerializer.Deserialize<EditorProjectState>(json, JsonOptions) ?? new EditorProjectState();
        }
        return new EditorProjectState();
    }

    private static async Task SaveEditorProjectStateAsync(string projectPath, EditorProjectState state)
    {
        var stateDir = Path.Combine(projectPath, ".galnet");
        Directory.CreateDirectory(stateDir);
        var statePath = Path.Combine(stateDir, "editor-state.json");
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await File.WriteAllTextAsync(statePath, json);
    }

    private static string NormalizeProjectPath(string projectPath) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectPath));
}
