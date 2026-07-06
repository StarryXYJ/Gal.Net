using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Services;
using GalNet.Editor.Project;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GalNet.Editor.ViewModels;

/// <summary>
/// 启动页面 ViewModel —— 类似 VS / Rider 的欢迎页。
/// 提供新建项目、打开项目、最近项目列表等功能。
/// </summary>
public partial class StartupPageViewModel : PageViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly IProjectService _projectService;
    private readonly IServiceProvider _serviceProvider;

    public StartupPageViewModel(INavigationService navigation, IProjectService projectService, IServiceProvider serviceProvider)
    {
        _navigation = navigation;
        _projectService = projectService;
        _serviceProvider = serviceProvider;
        Title = "GalNet Editor";

        RefreshRecentProjects();
    }

    /// <summary>最近项目列表（用于 UI 绑定）</summary>
    public ObservableCollection<RecentProjectItem> RecentProjects { get; } = new();

    /// <summary>刷新最近项目列表</summary>
    private void RefreshRecentProjects()
    {
        RecentProjects.Clear();
        foreach (var info in _projectService.GetRecentProjects())
        {
            RecentProjects.Add(new RecentProjectItem
            {
                Name = info.Name,
                Path = info.Path,
                LastOpened = info.LastOpened.ToString("yyyy-MM-dd HH:mm")
            });
        }
    }

    /// <summary>新建项目</summary>
    [RelayCommand]
    private async Task NewProjectAsync()
    {
        try
        {
            // 简化版：创建 Demo 项目
            var defaultPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "GalNetProjects", "NewProject");

            var basePath = defaultPath;
            var counter = 1;
            while (Directory.Exists(defaultPath))
            {
                defaultPath = basePath + counter;
                counter++;
            }

            var settings = new Core.Settings.ProjectSettings();
            var program = await _projectService.CreateAsync(defaultPath, "新项目", settings);

            NavigateToEditor(program);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create new project");
        }
    }

    /// <summary>打开已有项目</summary>
    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        try
        {
            // 使用系统文件夹选择对话框
            var dialog = new Avalonia.Controls.Window();
            var folders = await dialog.StorageProvider.OpenFolderPickerAsync(
                new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = "选择项目目录",
                    AllowMultiple = false
                });

            if (folders.Count == 0) return;

            var projectPath = folders[0].Path.LocalPath;
            var program = await _projectService.OpenAsync(projectPath);
            NavigateToEditor(program);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open project");
        }
    }

    /// <summary>打开最近项目</summary>
    [RelayCommand]
    private async Task OpenRecentProjectAsync(RecentProjectItem? item)
    {
        if (item == null) return;

        try
        {
            if (!Directory.Exists(item.Path))
            {
                Log.Warning("Recent project path no longer exists: {Path}", item.Path);
                _projectService.RemoveRecentProject(item.Path);
                RefreshRecentProjects();
                return;
            }

            var program = await _projectService.OpenAsync(item.Path);
            NavigateToEditor(program);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open recent project: {Path}", item.Path);
        }
    }

    /// <summary>从最近列表中删除</summary>
    [RelayCommand]
    private void RemoveRecentProject(RecentProjectItem? item)
    {
        if (item == null) return;
        _projectService.RemoveRecentProject(item.Path);
        RefreshRecentProjects();
    }

    private void NavigateToEditor(GalProject project)
    {
        var editorVm = _serviceProvider.GetRequiredService<EditorPageViewModel>();
        _navigation.NavigateTo(editorVm);
    }
}

/// <summary>最近项目列表项的 ViewModel</summary>
public partial class RecentProjectItem : ObservableObject
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string LastOpened { get; set; } = "";
}
