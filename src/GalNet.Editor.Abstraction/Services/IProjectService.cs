using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GalNet.Core.Settings;
using GalNet.Editor.Abstraction.Project;

namespace GalNet.Editor.Abstraction.Services;

/// <summary>
/// 项目管理 —— 编辑器全局单例。
/// 负责新建/打开/关闭项目，管理 GalProject 生命周期。
/// </summary>
public interface IProjectService
{
    /// <summary>当前打开的项目（null 表示未打开），同时只能有一个</summary>
    GalProject? Current { get; }

    /// <summary>项目状态变更通知</summary>
    event Action<GalProject?>? CurrentChanged;

    /// <summary>打开已有项目（如果有已打开项目则先关闭）</summary>
    Task<GalProject> OpenAsync(string projectPath);

    /// <summary>新建项目</summary>
    Task<GalProject> CreateAsync(string projectPath, string name, ProjectSettings settings);

    /// <summary>关闭当前项目（含脏检查）</summary>
    Task CloseAsync();

    /// <summary>保存当前项目</summary>
    Task SaveAsync();

    /// <summary>最近项目列表</summary>
    IReadOnlyList<RecentProjectInfo> GetRecentProjects();

    /// <summary>从最近项目列表移除一项</summary>
    void RemoveRecentProject(string projectPath);

    /// <summary>检查是否有未保存修改</summary>
    Task<bool> CheckUnsavedChangesAsync();
}