using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GalNet.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GalNet.Editor.Project;

/// <summary>
/// 项目管理服务 —— 编辑器全局单例。
/// </summary>
public sealed class ProjectService : IProjectService
{
    private readonly IServiceProvider _globalServices;
    private readonly string _editorSettingsPath;
    private readonly Lazy<EditorSettings> _editorSettings;

    private GalProject? _current;

    public GalProject? Current => _current;

    public event Action<GalProject?>? CurrentChanged;

    public ProjectService(IServiceProvider globalServices)
    {
        _globalServices = globalServices;
        _editorSettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GalNet", "editor-settings.json");

        // 延迟加载，避免构造函数中执行同步 IO
        _editorSettings = new Lazy<EditorSettings>(LoadEditorSettings);
    }

    // ────────── 编辑器设置持久化 ──────────

    private EditorSettings LoadEditorSettings()
    {
        try
        {
            if (File.Exists(_editorSettingsPath))
            {
                var json = File.ReadAllText(_editorSettingsPath);
                return JsonSerializer.Deserialize<EditorSettings>(json) ?? new EditorSettings();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load editor settings, using defaults");
        }
        return new EditorSettings();
    }

    private void SaveEditorSettings()
    {
        try
        {
            var dir = Path.GetDirectoryName(_editorSettingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_editorSettings.Value, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_editorSettingsPath, json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save editor settings");
        }
    }

    // ────────── 项目管理 ──────────

    public async Task<GalProject> OpenAsync(string projectPath)
    {
        if (_current != null)
            await CloseAsync();

        projectPath = Path.GetFullPath(projectPath);

        if (!Directory.Exists(projectPath))
            throw new DirectoryNotFoundException($"Project directory not found: {projectPath}");

        var settings = await LoadProjectSettingsAsync(projectPath);
        var scope = _globalServices.CreateScope();

        var name = Path.GetFileName(projectPath);
        var id = name;

        var program = new GalProject(id, name, projectPath, settings, scope);

        _current = program;

        AddToRecentProjects(name, projectPath);
        SaveEditorSettings();

        Log.Information("Project opened: {Name} at {Path}", name, projectPath);
        CurrentChanged?.Invoke(program);

        return program;
    }

    public async Task<GalProject> CreateAsync(string projectPath, string name, ProjectSettings settings)
    {
        if (_current != null)
            await CloseAsync();

        projectPath = Path.GetFullPath(projectPath);

        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(Path.Combine(projectPath, "Graph"));
        Directory.CreateDirectory(Path.Combine(projectPath, "Assets", "Layer"));
        Directory.CreateDirectory(Path.Combine(projectPath, "Assets", "Audio"));
        Directory.CreateDirectory(Path.Combine(projectPath, "Assets", "Video"));
        Directory.CreateDirectory(Path.Combine(projectPath, "I18n"));
        Directory.CreateDirectory(Path.Combine(projectPath, "Output"));
        Directory.CreateDirectory(Path.Combine(projectPath, "Temp"));

        await SaveProjectSettingsAsync(projectPath, settings);

        var scope = _globalServices.CreateScope();
        var program = new GalProject(name, name, projectPath, settings, scope);

        _current = program;

        AddToRecentProjects(name, projectPath);
        SaveEditorSettings();

        Log.Information("Project created: {Name} at {Path}", name, projectPath);
        CurrentChanged?.Invoke(program);

        return program;
    }

    public async Task CloseAsync()
    {
        if (_current == null)
            return;

        Log.Information("Closing project: {Name}", _current.Name);

        _current.Dispose();
        _current = null;

        CurrentChanged?.Invoke(null);

        await Task.CompletedTask;
    }

    public Task SaveAsync()
    {
        if (_current == null)
            return Task.CompletedTask;

        return SaveProjectSettingsAsync(_current.RootPath, _current.Settings);
    }

    // ────────── 最近项目 ──────────

    public IReadOnlyList<RecentProjectInfo> GetRecentProjects()
    {
        return _editorSettings.Value.RecentProjects
            .OrderByDescending(r => r.LastOpened)
            .ToList()
            .AsReadOnly();
    }

    public void RemoveRecentProject(string projectPath)
    {
        _editorSettings.Value.RecentProjects.RemoveAll(r =>
            string.Equals(r.Path, projectPath, StringComparison.OrdinalIgnoreCase));
        SaveEditorSettings();
    }

    public Task<bool> CheckUnsavedChangesAsync()
    {
        return Task.FromResult(_current?.IsDirty == true);
    }

    private void AddToRecentProjects(string name, string projectPath)
    {
        _editorSettings.Value.RecentProjects.RemoveAll(r =>
            string.Equals(r.Path, projectPath, StringComparison.OrdinalIgnoreCase));

        _editorSettings.Value.RecentProjects.Add(new RecentProjectInfo
        {
            Name = name,
            Path = projectPath,
            LastOpened = DateTime.Now
        });

        while (_editorSettings.Value.RecentProjects.Count > _editorSettings.Value.MaxRecentProjects)
            _editorSettings.Value.RecentProjects.RemoveAt(0);
    }

    // ────────── 项目设置持久化 ──────────

    private static async Task<ProjectSettings> LoadProjectSettingsAsync(string projectPath)
    {
        var settingsPath = Path.Combine(projectPath, "settings.json");
        if (File.Exists(settingsPath))
        {
            var json = await File.ReadAllTextAsync(settingsPath);
            return JsonSerializer.Deserialize<ProjectSettings>(json) ?? new ProjectSettings();
        }
        return new ProjectSettings();
    }

    private static async Task SaveProjectSettingsAsync(string projectPath, ProjectSettings settings)
    {
        var settingsPath = Path.Combine(projectPath, "settings.json");
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(settingsPath, json);
    }
}
