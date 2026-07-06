using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace GalNet.Editor.Shared.Commands;

/// <summary>
/// 异步编辑器命令基类 —— 封装 AsyncRelayCommand。
/// 派生类在构造函数中调用 <see cref="InitializeCommand"/> 完成初始化。
/// </summary>
public abstract class AsyncEditorCommand : EditorCommand
{
    private AsyncRelayCommand? _command;

    /// <summary>获取封装的 AsyncRelayCommand。</summary>
    protected AsyncRelayCommand RelayCommand =>
        _command ?? throw new InvalidOperationException(
            $"{GetType().Name}: InitializeCommand() has not been called in constructor.");

    public override ICommand Command =>
        _command ?? throw new InvalidOperationException(
            $"{GetType().Name}: InitializeCommand() has not been called in constructor.");

    /// <summary>在派生类构造函数中调用，传入实际执行逻辑。</summary>
    protected void InitializeCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _command = canExecute != null
            ? new AsyncRelayCommand(execute, canExecute)
            : new AsyncRelayCommand(execute);
    }
}
