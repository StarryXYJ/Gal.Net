using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GalNet.Core.Settings;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using GalNet.Control.UI;

namespace GalNet.Editor.Shared.Services;

/// <summary>
/// 项目管理服务 —— 编辑器全局单例。
/// </summary>
public sealed class ProjectService : IProjectService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly IServiceProvider _globalServices;
    private readonly IEditorSettingsService _editorSettingsService;

    private GalProject? _current;

    public GalProject? Current => _current;

    public event Action<GalProject?>? CurrentChanged;

    public ProjectService(IServiceProvider globalServices, IEditorSettingsService editorSettingsService)
    {
        _globalServices = globalServices;
        _editorSettingsService = editorSettingsService;
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

        var settings = await LoadProjectSettingsAsync(projectPath);
        await EnsureUiProjectAsync(projectPath);
        VariableNameRules.Normalize(settings);
        var editorState = await LoadEditorProjectStateAsync(projectPath);
        var scope = _globalServices.CreateScope();

        var name = Path.GetFileName(projectPath);
        var id = name;

        var program = new GalProject(id, name, projectPath, settings, editorState, new FileUiProjectProvider(projectPath), scope);

        _current = program;

        AddToRecentProjects(name, projectPath);
        _editorSettingsService.SaveSettings();

        Log.Information("Project opened: {Name} at {Path}", name, projectPath);
        CurrentChanged?.Invoke(program);

        return program;
    }

    public async Task<GalProject> CreateAsync(string projectPath, string name, ProjectSettings settings)
    {
        if (_current != null)
            await CloseAsync();

        projectPath = NormalizeProjectPath(projectPath);

        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(Path.Combine(projectPath, "Graph"));
        Directory.CreateDirectory(Path.Combine(projectPath, "Assets", "Layer"));
        Directory.CreateDirectory(Path.Combine(projectPath, "Assets", "Audio"));
        Directory.CreateDirectory(Path.Combine(projectPath, "Assets", "Video"));
        Directory.CreateDirectory(Path.Combine(projectPath, "I18n"));
        Directory.CreateDirectory(Path.Combine(projectPath, "Output"));
        Directory.CreateDirectory(Path.Combine(projectPath, "Temp"));
        Directory.CreateDirectory(Path.Combine(projectPath, ".galnet"));
        await EnsureUiProjectAsync(projectPath);

        await SaveProjectSettingsAsync(projectPath, settings);
        await SaveEditorProjectStateAsync(projectPath, new EditorProjectState());
        await SaveInitialGraphAsync(projectPath, name);

        var scope = _globalServices.CreateScope();
        var editorState = await LoadEditorProjectStateAsync(projectPath);
        var program = new GalProject(name, name, projectPath, settings, editorState, new FileUiProjectProvider(projectPath), scope);

        _current = program;

        AddToRecentProjects(name, projectPath);
        _editorSettingsService.SaveSettings();

        Log.Information("Project created: {Name} at {Path}", name, projectPath);
        CurrentChanged?.Invoke(program);

        return program;
    }

    public async Task CloseAsync()
    {
        if (_current == null)
            return;

        var closingProject = _current;
        Log.Information("Closing project: {Name}", closingProject.Name);

        // Scoped services can raise notifications while being disposed. Clear the
        // current reference first so those notifications cannot resolve a disposed scope.
        _current = null;
        closingProject.Dispose();

        CurrentChanged?.Invoke(null);

        await Task.CompletedTask;
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

    private static async Task<ProjectSettings> LoadProjectSettingsAsync(string projectPath)
    {
        var settingsPath = Path.Combine(projectPath, "settings.json");
        if (File.Exists(settingsPath))
        {
            var json = await File.ReadAllTextAsync(settingsPath);
            return JsonSerializer.Deserialize<ProjectSettings>(json, JsonOptions) ?? new ProjectSettings();
        }
        return new ProjectSettings();
    }

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

    private static async Task SaveInitialGraphAsync(string projectPath, string name)
    {
        var graphPath = Path.Combine(projectPath, "Graph");
        Directory.CreateDirectory(Path.Combine(graphPath, "groups"));

        var entryId = Guid.NewGuid().ToString("N");
        var groupId = Guid.NewGuid().ToString("N");
        var graph = new EditorGraphDocument
        {
            Name = name,
            RootNodeId = entryId,
            Nodes =
            [
                new EditorGraphNodeDto
                {
                    Id = entryId,
                    Type = "Entry",
                    Name = "Entry",
                    X = 4620,
                    Y = 4950
                },
                new EditorGraphNodeDto
                {
                    Id = groupId,
                    Type = "Group",
                    Name = "Opening",
                    X = 4900,
                    Y = 4950,
                    File = $"groups/{groupId}.galgroup"
                }
            ],
            Edges =
            [
                new EditorGraphEdgeDto
                {
                    FromNodeId = entryId,
                    FromOutlet = 0,
                    ToNodeId = groupId
                }
            ]
        };

        await File.WriteAllTextAsync(
            Path.Combine(graphPath, "graph.json"),
            JsonSerializer.Serialize(graph, JsonOptions));
        await File.WriteAllTextAsync(
            Path.Combine(graphPath, "groups", $"{groupId}.galgroup"),
            GalNet.Core.Serialization.GalgroupParser.Serialize("text", new Dictionary<string, string>
            {
                ["speaker"] = "Alice",
                ["text"] = "Hello GalNet"
            }));
    }

    /// <summary>One-time, deliberate migration for projects created before the UI project existed.</summary>
    private static async Task EnsureUiProjectAsync(string projectPath)
    {
        if (File.Exists(Path.Combine(projectPath, "UI", "ui.json")))
            return;
        var ui = new FileUiProjectProvider(projectPath, UiProjectDefaults.Create());
        await ui.SaveAsync();
    }

    private static string NormalizeProjectPath(string projectPath) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectPath));
}
