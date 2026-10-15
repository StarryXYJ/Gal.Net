using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GalNet.Core.Settings;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
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

        Commands.ProjectAuthorFileTransaction.Recover(projectPath);

        var scope = _globalServices.CreateScope();
        ProjectSettings settings;
        EditorProjectState editorState;
        string name;
        try
        {
            scope.ServiceProvider.GetRequiredService<ProjectSchemaSession>().Open(projectPath);
            settings = Commands.DirectProjectPersistence.LoadSettings(projectPath);
            editorState = await LoadEditorProjectStateAsync(projectPath);
            var loaded = scope.ServiceProvider.GetRequiredService<IEditorDocumentRepository>().Load(projectPath, "", settings);
            if (loaded.Document.Nodes.Count(node => node.Type == "Entry") != 1 ||
                !loaded.Document.Nodes.Any(node => node.Type == "Entry" && node.Id == loaded.Document.RootNodeId))
                throw new InvalidDataException("The editor graph requires one explicit Entry root node.");
            scope.ServiceProvider.GetRequiredService<IEditorDocumentService>().Load(loaded);
            name = loaded.Document.Name;
        }
        catch { scope.Dispose(); throw; }

        var id = Path.GetFileName(projectPath);

        var program = new GalProject(id, name, projectPath, settings, editorState, scope);
        scope.ServiceProvider.GetService<IProjectContentSession>()?.Open(program);

        _current = program;

        Log.Information("Project opened: {Name} at {Path}", name, projectPath);
        var recent = _editorSettingsService.GetSettings().RecentProjects;
        var previousRecent = recent.ToArray();
        try
        {
            CurrentChanged?.Invoke(program);
            AddToRecentProjects(name, projectPath);
            _editorSettingsService.SaveSettings();
        }
        catch
        {
            recent.Clear();
            recent.AddRange(previousRecent);
            await CloseCoreAsync(false);
            throw;
        }

        return program;
    }

    public async Task<GalProject> CreateAsync(string projectPath, string name, ProjectSettings settings, string contractsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractsDirectory);
        if (_current is not null) await CloseAsync();
        await Commands.EditorProjectCreator.CreateAsync(new(projectPath, name, contractsDirectory), settings);
        return await OpenAsync(projectPath);
    }
    private Task? _closing;
    public Task CloseAsync() => _closing ??= CloseRequestedAsync();

    private async Task CloseRequestedAsync()
    {
        // Always yield so the shared in-flight task is installed before it can finish.
        await Task.Yield();
        var project = _current;
        if (project is null) { _closing = null; return; }
        var content = project.Services.GetService<IProjectContentSession>();
        var participants = project.Services.GetServices<IProjectEditLifetime>().ToArray();
        try
        {
            foreach (var participant in participants) await participant.SuspendAsync();
            if (content is not null) await content.SuspendAsync();
            while (content?.IsDirty ?? project.IsDirty)
            {
                var choice = _globalServices.GetService<IProjectClosePrompt>() is { } prompt
                    ? await prompt.ChooseAsync(project.Name) : ProjectCloseChoice.Cancel;
                if (choice == ProjectCloseChoice.Cancel) throw new OperationCanceledException("Project close was canceled.");
                if (choice == ProjectCloseChoice.Discard) break;
                content?.ResumeSaves();
                foreach (var participant in participants) participant.ResumeEditing();
                await SaveAsync();
                foreach (var participant in participants) await participant.SuspendAsync();
                if (content is not null) await content.SuspendAsync();
            }
            await CloseCoreAsync(false);
        }
        catch { content?.ResumeSaves(); foreach (var participant in participants) participant.ResumeEditing(); throw; }
        finally { _closing = null; }
    }

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
        // Deletion and failed-open cleanup also enter here, bypassing the interactive close request.
        foreach (var participant in closingProject.Services.GetServices<IProjectEditLifetime>()) await participant.SuspendAsync();
        if (closingProject.Services.GetService<IProjectContentSession>() is { } content) await content.SuspendAsync();
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
        return _current.Services.GetRequiredService<IProjectContentSession>().SaveAsync();
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
        return Task.FromResult(_current?.Services.GetService<IProjectContentSession>()?.IsDirty ?? _current?.IsDirty == true);
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

    private static string NormalizeProjectPath(string projectPath) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectPath));
}
