using System.Windows.Input;
using Avalonia.Input;

namespace GalNet.Editor.Shared.Commands;

/// <summary>
/// 编辑器命令抽象基类 —— 每个命令封装自己的快捷键标识和 ICommand。
/// 派生类由 DI 容器创建，CommandService 统一管理。
/// </summary>
public abstract class EditorCommand
{
    /// <summary>唯一标识，用于序列化快捷键配置</summary>
    public string Id { get; protected set; } = string.Empty;

    /// <summary>显示名称</summary>
    public string DisplayName { get; protected set; } = string.Empty;

    /// <summary>默认快捷键</summary>
    public KeyGesture? DefaultGesture { get; protected set; }

    /// <summary>当前快捷键（可能被 CommandConfig 覆盖）</summary>
    public KeyGesture? Gesture { get; set; }

    /// <summary>可执行的命令</summary>
    public abstract ICommand Command { get; }
}
