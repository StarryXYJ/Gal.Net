using System;
using System.IO;
using GalNet.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Project;

/// <summary>
/// 编辑器打开中的一个游戏工程。
/// 承载工程元数据、路径、项目设置、项目级 DI Scope。
/// 注意：编辑器级设置（字体、主题、快捷键等）不在此处，走全局 ISettingsService。
/// </summary>
public sealed class GalProject : IDisposable
{
    /// <summary>工程唯一 ID</summary>
    public string Id { get; }

    /// <summary>工程显示名称</summary>
    public string Name { get; }

    /// <summary>工程根目录绝对路径</summary>
    public string RootPath { get; }

    /// <summary>Assets 根目录</summary>
    public string AssetsPath => Path.Combine(RootPath, "Assets");

    /// <summary>节点图目录</summary>
    public string GraphPath => Path.Combine(RootPath, "Graph");

    /// <summary>图形资源目录 (Layer)</summary>
    public string LayerPath => Path.Combine(AssetsPath, "Layer");

    /// <summary>音频资源目录</summary>
    public string AudioPath => Path.Combine(AssetsPath, "Audio");

    /// <summary>视频资源目录</summary>
    public string VideoPath => Path.Combine(AssetsPath, "Video");

    /// <summary>国际化文本目录</summary>
    public string I18nPath => Path.Combine(RootPath, "I18n");

    /// <summary>输出目录（打包导出）</summary>
    public string OutputPath => Path.Combine(RootPath, "Output");

    /// <summary>临时/缓存目录</summary>
    public string TempPath => Path.Combine(RootPath, "Temp");

    /// <summary>设置文件路径</summary>
    public string SettingsFilePath => Path.Combine(RootPath, "settings.json");

    public string EditorStateDirectory => Path.Combine(RootPath, ".galnet");

    public string EditorStateFilePath => Path.Combine(EditorStateDirectory, "editor-state.json");

    /// <summary>项目设置（持久化在 settings.json，每个项目一份）</summary>
    public ProjectSettings Settings { get; }

    public EditorProjectState EditorState { get; }

    /// <summary>项目级 DI Scope。从此 Scope 解析的 Service 在项目关闭时自动 Dispose。</summary>
    public IServiceScope Scope { get; }

    /// <summary>项目级 ServiceProvider</summary>
    public IServiceProvider Services => Scope.ServiceProvider;

    /// <summary>工程项目是否有未保存的修改</summary>
    public bool IsDirty { get; set; }

    public GalProject(string id, string name, string rootPath, ProjectSettings settings, EditorProjectState editorState, IServiceScope scope)
    {
        Id = id;
        Name = name;
        RootPath = rootPath;
        Settings = settings;
        EditorState = editorState;
        Scope = scope;
    }

    public void Dispose()
    {
        Scope.Dispose();
    }
}
