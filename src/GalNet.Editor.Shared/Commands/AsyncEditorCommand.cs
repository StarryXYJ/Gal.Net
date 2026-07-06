using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace GalNet.Editor.Shared.Commands;

/// <summary>
/// 异步编辑器命令基类 —— 封装 AsyncRelayCommand。
/// 派生类在构造函数中设置 _command。
/// </summary>
public abstract class AsyncEditorCommand : EditorCommand
{
    protected AsyncRelayCommand? _command;

    public override ICommand Command => _command!;
}
